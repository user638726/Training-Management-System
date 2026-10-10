<#
.SYNOPSIS
Builds and runs isolated HTTP integration checks for TrainingManagement.Api.
.DESCRIPTION
The script launches its own API process on port 5081 and creates a uniquely
named SQL Server database using Windows authentication. SQL Server and sqlcmd
must already be installed. It never sends test mutations to an existing API
or reuses an existing database. Bootstrap credentials and JWT signing keys are
generated in memory for this run. Failed runs keep their database and logs;
successful runs remove only their own database and temporary directory unless
-KeepArtifacts is supplied.
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Invoke-SmokeTest.ps1
.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Invoke-SmokeTest.ps1 -SqlServerInstance localhost -NoBuild -KeepArtifacts
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $SqlServerInstance = 'localhost',
    [switch] $NoBuild,
    [switch] $KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $workspaceRoot 'backend\TrainingManagement.Api.csproj'
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.tmp'))
$runId = [Guid]::NewGuid().ToString('N')
$runToken = $runId.Substring(0, 8)
$runDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot $runId))
$databaseName = "TrainingManagement_Test_$runId"
$databaseCreatedByThisRun = $false
$sqlcmdPath = $null
$Port = 5081
$baseUrl = "http://localhost:$Port"
$apiProcess = $null
$httpClient = $null
$passed = $false
$assertionCount = 0
$signingKey = $null
$adminPassword = $null
$accessToken = $null
$secondToken = $null
$registeredPassword = $null
$employeeToken = $null

function Protect-DiagnosticText {
    param([string] $Text)
    foreach ($secret in @($script:signingKey, $script:adminPassword,
        $script:accessToken, $script:secondToken, $script:registeredPassword,
        $script:employeeToken)) {
        if (-not [string]::IsNullOrEmpty($secret)) { $Text = $Text.Replace($secret, '[redacted]') }
    }
    return [regex]::Replace($Text,
        'eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', '[JWT redacted]')
}

function New-RandomSecret {
    param([int] $ByteCount = 48)
    $bytes = New-Object byte[] $ByteCount
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) }
    finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function Invoke-TestSql {
    param([string] $Query)
    $output = @(& $script:sqlcmdPath -S $script:SqlServerInstance -E -N -C -b `
        -l 10 -t 30 -d master -h -1 -W -Q $Query 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $diagnostic = Protect-DiagnosticText ($output -join [Environment]::NewLine)
        throw "SQL Server test operation failed: $diagnostic"
    }
    return $output
}

function ConvertTo-Base64Url {
    param([byte[]] $Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Read-JwtClaims {
    param([string] $Token)
    $parts = $Token.Split('.')
    if ($parts.Length -ne 3) { throw 'The login response did not contain a three-part JWT.' }
    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    $payload = $payload.PadRight($payload.Length + ((4 - $payload.Length % 4) % 4), '=')
    return ConvertFrom-Json -InputObject (
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)))
}

function New-SignedTestJwt {
    param([object] $Claims)
    $header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(
        (ConvertTo-Json -InputObject $Claims -Depth 12 -Compress)))
    $unsigned = "$header.$payload"
    $hasher = [Security.Cryptography.HMACSHA256]::new(
        [Text.Encoding]::UTF8.GetBytes($script:signingKey))
    try { $signature = ConvertTo-Base64Url ($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))) }
    finally { $hasher.Dispose() }
    return "$unsigned.$signature"
}

function Assert-That {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
    $script:assertionCount++
}

function Start-TestApiProcess {
    param([hashtable] $Configuration, [string] $StandardOutput, [string] $StandardError)
    $previousConfiguration = @{}
    try {
        foreach ($key in $Configuration.Keys) {
            $previousConfiguration[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $Configuration[$key], 'Process')
        }
        return Start-Process -FilePath 'dotnet' -ArgumentList @(
            ('"' + $script:assemblyPath + '"'), '--urls', $script:baseUrl, '--LegacyImportPath='
        ) -WorkingDirectory (Split-Path $script:projectPath -Parent) -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $StandardOutput -RedirectStandardError $StandardError
    }
    finally {
        foreach ($key in $previousConfiguration.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousConfiguration[$key], 'Process')
        }
    }
}

function Stop-TestApiProcess {
    if ($null -ne $script:apiProcess) {
        $script:apiProcess.Refresh()
        if (-not $script:apiProcess.HasExited) {
            # Stop only this run's directly launched API process.
            Stop-Process -InputObject $script:apiProcess -Force
            $script:apiProcess.WaitForExit()
        }
        $script:apiProcess.Dispose()
        $script:apiProcess = $null
    }
}

function Wait-TestApiReady {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($script:apiProcess.HasExited) { throw 'The test API process exited before becoming ready.' }
        try {
            $null = Invoke-Api -Method GET -Path '/api/health'
            return
        }
        catch { Start-Sleep -Milliseconds 250 }
    }
    throw 'The test API did not become ready within 60 seconds.'
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [object] $Body = $null,
        [int] $ExpectedStatus = 200,
        [string] $BearerToken,
        [ref] $ResponseHeaders
    )

    $request = [Net.Http.HttpRequestMessage]::new(
        [Net.Http.HttpMethod]::new($Method), "$script:baseUrl$Path")
    if ($PSBoundParameters.ContainsKey('BearerToken')) {
        $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $BearerToken)
    }
    if ($null -ne $Body) {
        $json = ConvertTo-Json -InputObject $Body -Depth 12 -Compress
        $request.Content = [Net.Http.StringContent]::new(
            $json, [Text.Encoding]::UTF8, 'application/json')
    }

    $response = $null
    try {
        $response = $script:httpClient.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $actualStatus = [int] $response.StatusCode
        if ($PSBoundParameters.ContainsKey('ResponseHeaders')) {
            $headers = @{}
            foreach ($header in $response.Headers) { $headers[$header.Key] = $header.Value -join ', ' }
            $ResponseHeaders.Value = $headers
        }
        if ($actualStatus -ne $ExpectedStatus) {
            $diagnostic = if ($Path -in @('/api/auth/login', '/api/auth/register')) { '[authentication response omitted]' }
                else { Protect-DiagnosticText $text }
            throw "$Method $Path expected HTTP $ExpectedStatus, received $actualStatus. Body: $diagnostic"
        }
        $script:assertionCount++
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $parsed = ConvertFrom-Json -InputObject $text
            # Windows PowerShell 5.1 preserves JSON arrays as a single pipeline
            # object. Emit their members explicitly for consistent collection
            # behavior in Windows PowerShell and PowerShell 7.
            if ($parsed -is [Array]) {
                foreach ($item in $parsed) { Write-Output $item }
            }
            else { return $parsed }
        }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose()
    }
}

function New-EmployeeBody {
    param([string] $Suffix, [bool] $IsActive = $true)
    return @{
        employeeNumber = "TEST-$script:runToken-$Suffix"
        name = "Integration employee $Suffix"
        email = "$script:runId-$Suffix@example.test"
        department = "Integration $script:runId"
        jobTitle = 'Training tester'
        isActive = $IsActive
    }
}

function New-CourseBody {
    param([string] $Status = 'Published', [int] $Capacity = 1)
    $start = [DateTimeOffset]::UtcNow.AddDays(7)
    return @{
        title = "Integration course $script:runId $Status"
        description = 'Isolated API smoke test course'
        category = 'Integration'
        instructor = 'Test instructor'
        location = 'Test classroom'
        format = 'Hybrid'
        startDate = $start.ToString('o')
        endDate = $start.AddHours(3.5).ToString('o')
        durationHours = 3.5
        capacity = $Capacity
        status = $Status
    }
}

function New-RegistrationBody {
    param([string] $Suffix = 'employee')
    return @{
        username = "$Suffix-$script:runToken"
        displayName = 'Smoke Test Employee'
        password = $script:registeredPassword
        confirmPassword = $script:registeredPassword
    }
}

try {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "API project was not found: $projectPath"
    }

    # Check both localhost addresses before starting so an existing API cannot
    # receive test mutations through either IPv4 or IPv6.
    $portProbes = @([Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port))
    if ([Net.Sockets.Socket]::OSSupportsIPv6) {
        $portProbes += [Net.Sockets.TcpListener]::new([Net.IPAddress]::IPv6Loopback, $Port)
    }
    try { foreach ($probe in $portProbes) { $probe.Start() } }
    catch { throw "Port $Port is already in use. Stop that service before running isolated tests." }
    finally { foreach ($probe in $portProbes) { $probe.Stop() } }

    if (-not $NoBuild) {
        Write-Host 'Building the API...'
        & dotnet build $projectPath --configuration Debug --nologo --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "API build failed with exit code $LASTEXITCODE." }
    }

    [xml] $projectXml = Get-Content -LiteralPath $projectPath -Raw
    $targetFramework = @($projectXml.Project.PropertyGroup.TargetFramework |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })[0]
    $assemblyPath = Join-Path (Split-Path $projectPath -Parent) (
        "bin\Debug\$targetFramework\TrainingManagement.Api.dll")
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "Built API assembly was not found: $assemblyPath. Run without -NoBuild."
    }

    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $sqlcmdPath = (Get-Command sqlcmd -ErrorAction Stop).Source
    if ($databaseName -notmatch '^TrainingManagement_Test_[a-f0-9]{32}$') {
        throw 'The generated SQL Server test database name failed its safety guard.'
    }
    # The CREATE succeeds only if this generated name did not already exist.
    # Cleanup later requires both this success marker and the strict name guard.
    $null = Invoke-TestSql (
        "IF DB_ID(N'$databaseName') IS NOT NULL THROW 50000, 'Refusing to reuse an existing database.', 1; " +
        "CREATE DATABASE [$databaseName];")
    $databaseCreatedByThisRun = $true
    $connectionBuilder = [Data.Common.DbConnectionStringBuilder]::new()
    $connectionBuilder.Add('Server', $SqlServerInstance)
    $connectionBuilder.Add('Database', $databaseName)
    $connectionBuilder.Add('Trusted_Connection', 'True')
    $connectionBuilder.Add('Encrypt', 'True')
    $connectionBuilder.Add('TrustServerCertificate', 'True')
    $connectionBuilder.Add('Connect Timeout', '10')
    $signingKey = New-RandomSecret
    $adminUsername = "smoke-$runToken"
    $adminPassword = 'Aa1!' + (New-RandomSecret -ByteCount 24)
    $registeredPassword = 'Aa1!' + (New-RandomSecret -ByteCount 24)
    $adminDisplayName = 'Smoke Test Admin'
    $stdoutPath = Join-Path $runDirectory 'api.stdout.log'
    $stderrPath = Join-Path $runDirectory 'api.stderr.log'
    $processConfiguration = @{
        ConnectionStrings__TrainingDatabase = $connectionBuilder.ConnectionString
        ASPNETCORE_ENVIRONMENT = 'Development'
        SeedDemoData = 'true'
        LegacyImportPath = ''
        BootstrapAdmin__Username = $adminUsername
        BootstrapAdmin__Password = $adminPassword
        BootstrapAdmin__DisplayName = $adminDisplayName
        Jwt__SigningKey = $signingKey
        Jwt__Issuer = 'TrainingManagement.Api'
        Jwt__Audience = 'TrainingManagement.Web'
        Jwt__ExpiresMinutes = '30'
        # The production default is five registrations per minute. This
        # isolated process raises only that limit to exercise validation cases
        # and concurrent duplicates without waiting through multiple windows.
        RateLimits__RegisterPermitLimit = '100'
    }
    $apiProcess = Start-TestApiProcess -Configuration $processConfiguration `
        -StandardOutput $stdoutPath -StandardError $stderrPath

    Add-Type -AssemblyName System.Net.Http
    $httpClient = [Net.Http.HttpClient]::new()
    $httpClient.Timeout = [TimeSpan]::FromSeconds(10)
    Wait-TestApiReady
    $migrationTableCount = ((Invoke-TestSql (
        "SET NOCOUNT ON; SELECT COUNT(*) FROM [$databaseName].sys.tables " +
        "WHERE name = N'__EFMigrationsHistory';")) -join '').Trim()
    Assert-That ($databaseCreatedByThisRun -and $migrationTableCount -eq '1') `
        'The API migrated the SQL Server database created exclusively for this run.'

    Write-Host "Checking the API at $baseUrl using isolated SQL Server database $databaseName..."
    foreach ($protectedPath in @('/api/courses', '/api/employees', '/api/enrollments',
        '/api/dashboard', '/api/auth/me')) {
        $null = Invoke-Api -Method GET -Path $protectedPath -ExpectedStatus 401
    }
    $null = Invoke-Api -Method POST -Path '/api/employees' `
        -Body (New-EmployeeBody 'unauthorized') -ExpectedStatus 401
    $null = Invoke-Api -Method POST -Path '/api/enrollments/1/cancel' -ExpectedStatus 401

    $null = Invoke-Api -Method POST -Path '/api/auth/register' -Body @{} -ExpectedStatus 400
    $invalidRegistrations = @(
        @{ field = 'username'; value = 'ab' },
        @{ field = 'username'; value = 'invalid username!' },
        @{ field = 'username'; value = ('a' * 81) },
        @{ field = 'displayName'; value = '   ' },
        @{ field = 'displayName'; value = ('D' * 101) },
        @{ field = 'password'; value = 'Aa1!short' },
        @{ field = 'password'; value = (' ' * 12) },
        @{ field = 'password'; value = ('X' * 257) },
        @{ field = 'confirmPassword'; value = 'mismatched confirmation' }
    )
    foreach ($invalidRegistration in $invalidRegistrations) {
        $invalidBody = New-RegistrationBody -Suffix 'invalid'
        $invalidBody[$invalidRegistration.field] = $invalidRegistration.value
        if ($invalidRegistration.field -eq 'password') {
            $invalidBody.confirmPassword = $invalidRegistration.value
        }
        $null = Invoke-Api -Method POST -Path '/api/auth/register' -Body $invalidBody -ExpectedStatus 400
    }
    foreach ($spoof in @(
        @{ field = 'role'; value = 'Admin' },
        @{ field = 'id'; value = 2147483647 },
        @{ field = 'isActive'; value = $false },
        @{ field = 'tokenVersion'; value = 999 }
    )) {
        $spoofBody = New-RegistrationBody -Suffix 'spoof'
        $spoofBody[$spoof.field] = $spoof.value
        $null = Invoke-Api -Method POST -Path '/api/auth/register' -Body $spoofBody -ExpectedStatus 400
    }
    $registeredBody = New-RegistrationBody
    $registeredBody.displayName = '  Smoke Test Employee  '
    $registeredUser = Invoke-Api -Method POST -Path '/api/auth/register' -Body $registeredBody -ExpectedStatus 201
    Assert-That ($registeredUser.id -gt 0 -and $registeredUser.id -lt 2147483647 -and
        $registeredUser.role -eq 'Employee') 'Registration assigns its own ID and Employee role.'
    Assert-That ($registeredUser.username -eq $registeredBody.username -and
        $registeredUser.displayName -eq 'Smoke Test Employee') 'Registration trims the display name and returns the user.'
    Assert-That ($registeredUser.PSObject.Properties.Name -notcontains 'accessToken' -and
        $registeredUser.PSObject.Properties.Name -notcontains 'password' -and
        $registeredUser.PSObject.Properties.Name -notcontains 'passwordHash') `
        'Registration returns neither a JWT nor password data.'
    $null = Invoke-Api -Method POST -Path '/api/auth/register' -Body $registeredBody -ExpectedStatus 409
    $duplicateRegistration = New-RegistrationBody
    $duplicateRegistration.username = '  ' + $registeredBody.username.ToUpperInvariant() + '  '
    $null = Invoke-Api -Method POST -Path '/api/auth/register' -Body $duplicateRegistration -ExpectedStatus 409

    # Verify a salted password hash was persisted without printing the hash,
    # sending a password to sqlcmd, or putting credentials in command arguments.
    $normalizedRegisteredUsername = $registeredBody.username.ToUpperInvariant()
    $storedPasswordHash = ((Invoke-TestSql (
        "SET NOCOUNT ON; SELECT PasswordHash FROM [$databaseName].dbo.AppUsers " +
        "WHERE NormalizedUsername = N'$normalizedRegisteredUsername';")) -join '').Trim()
    Assert-That (-not [string]::IsNullOrWhiteSpace($storedPasswordHash) -and
        $storedPasswordHash -ne $registeredPassword -and $storedPasswordHash.Length -gt 80) `
        'The registered password is persisted as a hash.'
    $storedPasswordHash = $null

    $registrationRaceBody = New-RegistrationBody -Suffix 'race'
    $registrationRaceRequests = @()
    $registrationRaceTasks = @()
    $registrationRaceResponses = @()
    try {
        foreach ($attempt in 1..2) {
            $request = [Net.Http.HttpRequestMessage]::new(
                [Net.Http.HttpMethod]::Post, "$baseUrl/api/auth/register")
            $request.Content = [Net.Http.StringContent]::new(
                (ConvertTo-Json -InputObject $registrationRaceBody -Compress),
                [Text.Encoding]::UTF8, 'application/json')
            $registrationRaceRequests += $request
            $registrationRaceTasks += $httpClient.SendAsync($request)
        }
        foreach ($task in $registrationRaceTasks) {
            $registrationRaceResponses += $task.GetAwaiter().GetResult()
        }
        $registrationRaceStatuses = @($registrationRaceResponses | ForEach-Object { [int] $_.StatusCode })
        Assert-That (@($registrationRaceStatuses | Where-Object { $_ -eq 201 }).Count -eq 1) `
            'Concurrent duplicate registrations create exactly one user.'
        Assert-That (@($registrationRaceStatuses | Where-Object { $_ -eq 409 }).Count -eq 1) `
            'Concurrent duplicate registrations reject the other request with a conflict.'
    }
    finally {
        foreach ($response in $registrationRaceResponses) { $response.Dispose() }
        foreach ($request in $registrationRaceRequests) { $request.Dispose() }
    }
    $employeeLogin = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        username = $registeredBody.username; password = $registeredPassword
    }
    $employeeToken = $employeeLogin.accessToken
    $employeeClaims = Read-JwtClaims $employeeToken
    Assert-That ($employeeLogin.user.id -eq $registeredUser.id -and
        $employeeLogin.user.role -eq 'Employee' -and $employeeClaims.role -eq 'Employee' -and
        [int]$employeeClaims.token_version -eq 0) 'A registered user logs in with an Employee JWT and initial token version.'
    $employeeMe = Invoke-Api -Method GET -Path '/api/auth/me' -BearerToken $employeeToken
    Assert-That ($employeeMe.id -eq $registeredUser.id -and $employeeMe.role -eq 'Employee') `
        'The registered employee is active and can resolve their authenticated identity.'

    $null = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{} -ExpectedStatus 400
    $null = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        username = $adminUsername; password = 'incorrect-test-password'
    } -ExpectedStatus 401
    $login = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        username = $adminUsername; password = $adminPassword
    }
    $accessToken = $login.accessToken
    Assert-That ($login.tokenType -eq 'Bearer') 'Login returns the Bearer token type.'
    Assert-That (-not [string]::IsNullOrWhiteSpace($accessToken)) 'Login returns an access token.'
    $expiration = [DateTimeOffset] $login.expiresAt
    Assert-That ($expiration -gt [DateTimeOffset]::UtcNow -and
        $expiration -lt [DateTimeOffset]::UtcNow.AddMinutes(31)) 'The issued token has the configured expiration.'
    Assert-That ($login.user.username -eq $adminUsername -and $login.user.role -eq 'Admin' -and
        $login.user.displayName -eq $adminDisplayName) 'Login returns the bootstrapped administrator identity.'
    $claims = Read-JwtClaims $accessToken
    Assert-That ([string]$claims.sub -eq [string]$login.user.id -and $claims.name -eq $adminUsername -and
        $claims.role -eq 'Admin') 'JWT identity and role claims match the authenticated user.'
    Assert-That ($claims.iss -eq 'TrainingManagement.Api' -and $claims.aud -eq 'TrainingManagement.Web') `
        'JWT issuer and audience match the API configuration.'
    Assert-That ([Math]::Abs([long]$claims.exp - $expiration.ToUnixTimeSeconds()) -le 1) `
        'JWT expiration matches the expiration returned by login.'
    $httpClient.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new(
        'Bearer', $accessToken)
    $currentUser = Invoke-Api -Method GET -Path '/api/auth/me'
    Assert-That ($currentUser.id -eq $login.user.id -and $currentUser.username -eq $adminUsername -and
        $currentUser.role -eq 'Admin') 'An authenticated request resolves the current administrator.'

    $resignedUser = Invoke-Api -Method GET -Path '/api/auth/me' `
        -BearerToken (New-SignedTestJwt (Read-JwtClaims $accessToken))
    Assert-That ($resignedUser.id -eq $login.user.id) `
        'The signed-token test helper produces a valid signature before claim-rejection tests.'

    $tokenParts = $accessToken.Split('.')
    $replacementCharacter = if ($tokenParts[2][0] -eq 'A') { 'B' } else { 'A' }
    $alteredToken = $tokenParts[0] + '.' + $tokenParts[1] + '.' +
        $replacementCharacter + $tokenParts[2].Substring(1)
    $null = Invoke-Api -Method GET -Path '/api/dashboard' -BearerToken $alteredToken -ExpectedStatus 401
    $wrongIssuer = Read-JwtClaims $accessToken
    $wrongIssuer.iss = 'Untrusted.Issuer'
    $null = Invoke-Api -Method GET -Path '/api/dashboard' `
        -BearerToken (New-SignedTestJwt $wrongIssuer) -ExpectedStatus 401
    $wrongAudience = Read-JwtClaims $accessToken
    $wrongAudience.aud = 'Untrusted.Audience'
    $null = Invoke-Api -Method GET -Path '/api/dashboard' `
        -BearerToken (New-SignedTestJwt $wrongAudience) -ExpectedStatus 401
    $expiredClaims = Read-JwtClaims $accessToken
    $pastTime = [DateTimeOffset]::UtcNow.AddHours(-2).ToUnixTimeSeconds()
    $expiredClaims.iat = $pastTime
    $expiredClaims.exp = $pastTime + 3600
    if ($expiredClaims.PSObject.Properties.Name -contains 'nbf') { $expiredClaims.nbf = $pastTime }
    $null = Invoke-Api -Method GET -Path '/api/dashboard' `
        -BearerToken (New-SignedTestJwt $expiredClaims) -ExpectedStatus 401
    $seedEmployees = @(Invoke-Api -Method GET -Path '/api/employees')
    $seedCourses = @(Invoke-Api -Method GET -Path '/api/courses')
    $seedEnrollments = @(Invoke-Api -Method GET -Path '/api/enrollments')
    Assert-That ($seedEmployees.Count -gt 0) 'The database contains seed employees.'
    Assert-That ($seedCourses.Count -gt 0) 'The database contains seed courses.'
    $baseline = Invoke-Api -Method GET -Path '/api/dashboard'
    Assert-That ($baseline.employeeCount -eq @($seedEmployees | Where-Object { $_.isActive }).Count) `
        'Dashboard employee count matches active staff.'
    Assert-That ($baseline.courseCount -eq $seedCourses.Count) 'Dashboard course count matches records.'
    Assert-That ($baseline.enrollmentCount -eq @(
        $seedEnrollments | Where-Object { $_.status -ne 'Cancelled' }).Count) `
        'Dashboard enrollment count excludes cancelled records.'
    Assert-That ($baseline.completedEnrollmentCount -eq @(
        $seedEnrollments | Where-Object { $_.status -eq 'Completed' }).Count) `
        'Dashboard completion count matches completion records.'
    Assert-That (@($baseline.monthlyStats).Count -eq 6) 'Dashboard exposes the last six reporting months.'

    $invalidEmployee = New-EmployeeBody 'invalid'
    $invalidEmployee.name = ''
    $invalidResponse = Invoke-Api -Method POST -Path '/api/employees' `
        -Body $invalidEmployee -ExpectedStatus 400
    Assert-That (-not [string]::IsNullOrWhiteSpace($invalidResponse.title)) `
        'Validation errors have a readable ProblemDetails title.'

    $employeeBodyA = New-EmployeeBody 'a'
    $employeeBodyB = New-EmployeeBody 'b'
    $inactiveBody = New-EmployeeBody 'inactive' $false
    $employeeA = Invoke-Api -Method POST -Path '/api/employees' -Body $employeeBodyA -ExpectedStatus 201
    $employeeB = Invoke-Api -Method POST -Path '/api/employees' -Body $employeeBodyB -ExpectedStatus 201
    $inactiveEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body $inactiveBody -ExpectedStatus 201
    Assert-That ($employeeA.id -gt 0) 'A created employee receives an ID.'

    $duplicateNumber = New-EmployeeBody 'duplicate-number'
    $duplicateNumber.employeeNumber = $employeeBodyA.employeeNumber
    $null = Invoke-Api -Method POST -Path '/api/employees' -Body $duplicateNumber -ExpectedStatus 409
    $duplicateEmail = New-EmployeeBody 'duplicate-email'
    $duplicateEmail.email = $employeeBodyA.email
    $null = Invoke-Api -Method POST -Path '/api/employees' -Body $duplicateEmail -ExpectedStatus 409

    $invalidCourse = New-CourseBody
    $invalidCourse.endDate = ([DateTimeOffset]::Parse($invalidCourse.startDate).AddDays(-1)).ToString('o')
    $null = Invoke-Api -Method POST -Path '/api/courses' -Body $invalidCourse -ExpectedStatus 400
    $courseBody = New-CourseBody
    $draftBody = New-CourseBody -Status 'Draft'
    $course = Invoke-Api -Method POST -Path '/api/courses' -Body $courseBody -ExpectedStatus 201
    $draftCourse = Invoke-Api -Method POST -Path '/api/courses' -Body $draftBody -ExpectedStatus 201
    Assert-That ($course.enrolledCount -eq 0) 'A new course has no enrollments.'

    $null = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $draftCourse.id; employeeId = $employeeA.id
    } -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $inactiveEmployee.id
    } -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = 2147483647; employeeId = $employeeA.id
    } -ExpectedStatus 404

    $enrollmentA = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $employeeA.id
    } -ExpectedStatus 201
    Assert-That ($enrollmentA.status -eq 'Registered') 'New enrollments start as Registered.'
    Assert-That ($enrollmentA.progress -eq 0) 'New enrollments start at zero progress.'
    $null = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $employeeA.id
    } -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $employeeB.id
    } -ExpectedStatus 409

    $courseBody.capacity = 2
    $null = Invoke-Api -Method PUT -Path "/api/courses/$($course.id)" -Body $courseBody
    $enrollmentB = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $employeeB.id
    } -ExpectedStatus 201
    $courseBody.capacity = 1
    $null = Invoke-Api -Method PUT -Path "/api/courses/$($course.id)" `
        -Body $courseBody -ExpectedStatus 409
    $courseBody.capacity = 2

    $null = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentA.id)" -Body @{
        status = 'Completed'; progress = 50; score = 90
    } -ExpectedStatus 400
    $inProgress = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentA.id)" -Body @{
        status = 'InProgress'; progress = 60; score = $null
    }
    Assert-That ($inProgress.progress -eq 60) 'Progress updates are persisted.'
    $null = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentA.id)" `
        -Body @{} -ExpectedStatus 400
    $unchanged = Invoke-Api -Method GET -Path "/api/enrollments/$($enrollmentA.id)"
    Assert-That ($unchanged.status -eq 'InProgress' -and $unchanged.progress -eq 60 -and
        $null -eq $unchanged.completedAt) 'An incomplete update cannot reset existing enrollment data.'
    $completed = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentA.id)" -Body @{
        status = 'Completed'; progress = 100; score = 92
    }
    Assert-That ($completed.status -eq 'Completed') 'Completion status is persisted.'
    Assert-That ($completed.score -eq 92) 'Completion score is persisted.'
    Assert-That ($null -ne $completed.completedAt) 'Completion records include a completion timestamp.'

    $dashboard = Invoke-Api -Method GET -Path '/api/dashboard'
    Assert-That ($dashboard.employeeCount -eq $baseline.employeeCount + 2) `
        'Employee dashboard count includes added active staff.'
    Assert-That ($dashboard.courseCount -eq $baseline.courseCount + 2) 'Course dashboard count includes added courses.'
    Assert-That ($dashboard.enrollmentCount -eq $baseline.enrollmentCount + 2) 'Enrollment dashboard count includes both registrations.'
    Assert-That ($dashboard.completedEnrollmentCount -eq $baseline.completedEnrollmentCount + 1) `
        'Completion dashboard count includes the newly completed enrollment.'
    Assert-That ([Math]::Abs([double]$dashboard.totalTrainingHours -
        ([double]$baseline.totalTrainingHours + 3.5)) -lt 0.01) 'Training hours include completed course duration.'
    $department = @($dashboard.departmentStats | Where-Object { $_.department -eq $employeeBodyA.department })
    Assert-That ($department.Count -eq 1) 'Department reporting contains the test department.'
    Assert-That ($department[0].employeeCount -eq 2) 'Department employee report counts active staff.'
    Assert-That ($department[0].enrollmentCount -eq 2) 'Department report counts registrations.'
    Assert-That ($department[0].completedCount -eq 1) 'Department report counts completions.'
    Assert-That ([Math]::Abs([double]$department[0].trainingHours - 3.5) -lt 0.01) `
        'Department reporting sums completed training hours.'
    $completionInstant = [DateTimeOffset] $completed.completedAt
    $currentMonth = $completionInstant.ToOffset(
        [TimeSpan]::FromHours(8)).ToString('yyyy-MM')
    $completionMonth = @($dashboard.monthlyStats | Where-Object { $_.month -eq $currentMonth })
    $baselineMonth = @($baseline.monthlyStats | Where-Object { $_.month -eq $currentMonth })
    Assert-That ($completionMonth.Count -eq 1) 'Monthly report includes the completion month.'
    Assert-That ($completionMonth[0].completions -eq $baselineMonth[0].completions + 1) `
        'Monthly report adds the new completion.'

    $reopened = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentA.id)" -Body @{
        status = 'InProgress'; progress = 80; score = $null
    }
    Assert-That ($null -eq $reopened.completedAt) 'Reopening completion clears completedAt.'
    $reopenedDashboard = Invoke-Api -Method GET -Path '/api/dashboard'
    Assert-That ($reopenedDashboard.completedEnrollmentCount -eq $baseline.completedEnrollmentCount) `
        'Reopening completion reverses dashboard completion totals.'
    Assert-That ([Math]::Abs([double]$reopenedDashboard.totalTrainingHours -
        [double]$baseline.totalTrainingHours) -lt 0.01) 'Reopening completion reverses training hours.'

    $null = Invoke-Api -Method DELETE -Path "/api/employees/$($employeeA.id)" -ExpectedStatus 409
    $null = Invoke-Api -Method DELETE -Path "/api/courses/$($course.id)" -ExpectedStatus 409
    $null = Invoke-Api -Method PUT -Path "/api/enrollments/$($enrollmentB.id)" -Body @{
        status = 'Cancelled'; progress = 0; score = $null
    }
    $reregistered = Invoke-Api -Method POST -Path '/api/enrollments' -Body @{
        courseId = $course.id; employeeId = $employeeB.id
    } -ExpectedStatus 201
    Assert-That ($reregistered.id -eq $enrollmentB.id) 'Reenrollment reactivates the cancelled record.'
    Assert-That ($reregistered.status -eq 'Registered') 'Reenrollment restores Registered status.'

    # Both requests are dispatched before either is awaited, exercising the
    # last-seat check when multiple employees register at the same time.
    $raceBody = New-CourseBody -Capacity 1
    $raceBody.title += ' Concurrent seats'
    $raceCourse = Invoke-Api -Method POST -Path '/api/courses' -Body $raceBody -ExpectedStatus 201
    $raceRequests = @()
    $raceTasks = @()
    $raceResponses = @()
    try {
        foreach ($employeeId in @($employeeA.id, $employeeB.id)) {
            $request = [Net.Http.HttpRequestMessage]::new(
                [Net.Http.HttpMethod]::Post, "$baseUrl/api/enrollments")
            $request.Content = [Net.Http.StringContent]::new(
                (ConvertTo-Json -InputObject @{
                    courseId = $raceCourse.id; employeeId = $employeeId
                } -Compress), [Text.Encoding]::UTF8, 'application/json')
            $raceRequests += $request
            $raceTasks += $httpClient.SendAsync($request)
        }
        foreach ($raceTask in $raceTasks) {
            $raceResponses += $raceTask.GetAwaiter().GetResult()
        }
        $raceStatuses = @($raceResponses | ForEach-Object { [int] $_.StatusCode })
        Assert-That (@($raceStatuses | Where-Object { $_ -eq 201 }).Count -eq 1) `
            "Concurrent last-seat requests have exactly one success (received $($raceStatuses -join ', '))."
        Assert-That (@($raceStatuses | Where-Object { $_ -eq 409 }).Count -eq 1) `
            "Concurrent last-seat requests have exactly one conflict (received $($raceStatuses -join ', '))."
        $raceReadback = Invoke-Api -Method GET -Path "/api/courses/$($raceCourse.id)"
        Assert-That ($raceReadback.enrolledCount -eq 1) 'Concurrent registrations do not oversubscribe the course.'
    }
    finally {
        foreach ($raceResponse in $raceResponses) { $raceResponse.Dispose() }
        foreach ($raceRequest in $raceRequests) { $raceRequest.Dispose() }
    }

    $null = Invoke-Api -Method GET -Path '/api/accounts' -BearerToken $employeeToken -ExpectedStatus 403
    $accountEmployeeBody = New-EmployeeBody 'registered'
    $accountEmployeeBody.name = $registeredUser.displayName
    $null = Invoke-Api -Method POST -Path "/api/accounts/$($registeredUser.id)/employee" `
        -Body $accountEmployeeBody -BearerToken $employeeToken -ExpectedStatus 403
    $unlinkedMine = Invoke-Api -Method GET -Path '/api/enrollments/mine' -BearerToken $employeeToken
    Assert-That (@($unlinkedMine).Count -eq 0) 'Unlinked accounts have no personal training records.'
    $null = Invoke-Api -Method POST -Path '/api/enrollments/self' -Body @{ courseId = $course.id } `
        -BearerToken $employeeToken -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path "/api/enrollments/$($enrollmentA.id)/cancel" `
        -BearerToken $employeeToken -ExpectedStatus 409
    $accounts = Invoke-Api -Method GET -Path '/api/accounts'
    Assert-That (@($accounts | Where-Object { $_.id -eq $registeredUser.id }).Count -eq 1) `
        'Administrators can find a newly registered account awaiting employee creation.'
    $listedAccount = @($accounts | Where-Object { $_.id -eq $registeredUser.id })[0]
    Assert-That ($null -eq $listedAccount.employeeId -and $listedAccount.displayName -eq $registeredUser.displayName) `
        'The account list exposes a new registration as pending without creating a staff record.'
    $employeeCountBeforeApproval = @(Invoke-Api -Method GET -Path '/api/employees').Count
    $null = Invoke-Api -Method POST -Path "/api/accounts/$($registeredUser.id)/employee" `
        -Body @{} -ExpectedStatus 400
    $duplicateAccountEmployee = New-EmployeeBody 'duplicate-number'
    $duplicateAccountEmployee.employeeNumber = $employeeA.employeeNumber
    $null = Invoke-Api -Method POST -Path "/api/accounts/$($registeredUser.id)/employee" `
        -Body $duplicateAccountEmployee -ExpectedStatus 409
    $newEmployee = Invoke-Api -Method POST -Path "/api/accounts/$($registeredUser.id)/employee" `
        -Body $accountEmployeeBody -ExpectedStatus 201
    Assert-That ($newEmployee.id -gt 0 -and $newEmployee.id -ne $employeeA.id -and
        $newEmployee.id -ne $employeeB.id -and $newEmployee.employeeNumber -eq
        $accountEmployeeBody.employeeNumber.ToUpperInvariant()) `
        'Approving a registered account creates a distinct new employee record.'
    Assert-That (@(Invoke-Api -Method GET -Path '/api/employees').Count -eq $employeeCountBeforeApproval + 1) `
        'Account approval adds exactly one employee.'
    $null = Invoke-Api -Method POST -Path "/api/accounts/$($registeredUser.id)/employee" `
        -Body $accountEmployeeBody -ExpectedStatus 409
    $accountsAfterLink = Invoke-Api -Method GET -Path '/api/accounts'
    $listedLinkedAccount = @($accountsAfterLink | Where-Object { $_.id -eq $registeredUser.id })[0]
    Assert-That ($listedLinkedAccount.employeeId -eq $newEmployee.id) `
        'The account list points to the new employee after approval.'
    $linkedMe = Invoke-Api -Method GET -Path '/api/auth/me' -BearerToken $employeeToken
    Assert-That ($linkedMe.employeeId -eq $newEmployee.id) 'The existing JWT resolves the newly created employee.'
    $null = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $draftCourse.id } -BearerToken $employeeToken -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $course.id } -BearerToken $employeeToken -ExpectedStatus 409
    $null = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $course.id; employeeId = $employeeB.id } `
        -BearerToken $employeeToken -ExpectedStatus 400
    $selfCourseBody = New-CourseBody -Capacity 1
    $selfCourseBody.title += ' Self registration'
    $selfCourse = Invoke-Api -Method POST -Path '/api/courses' -Body $selfCourseBody -ExpectedStatus 201
    $selfEnrollment = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $selfCourse.id } -BearerToken $employeeToken -ExpectedStatus 201
    Assert-That ($selfEnrollment.employeeId -eq $newEmployee.id -and $selfEnrollment.status -eq 'Registered') `
        'An employee account can enroll only its newly created employee in a published course.'
    $null = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $selfCourse.id } -BearerToken $employeeToken -ExpectedStatus 409
    $mine = Invoke-Api -Method GET -Path '/api/enrollments/mine' -BearerToken $employeeToken
    Assert-That (@($mine | Where-Object { $_.id -eq $selfEnrollment.id }).Count -eq 1) `
        'The personal enrollment list includes the new self-registration.'
    $null = Invoke-Api -Method POST -Path "/api/enrollments/$($selfEnrollment.id)/cancel" `
        -ExpectedStatus 403
    $null = Invoke-Api -Method POST -Path "/api/enrollments/$($enrollmentB.id)/cancel" `
        -BearerToken $employeeToken -ExpectedStatus 403
    $otherAfterCancelAttempt = Invoke-Api -Method GET -Path "/api/enrollments/$($enrollmentB.id)"
    Assert-That ($otherAfterCancelAttempt.status -eq 'Registered') `
        'A user cannot cancel another employee’s enrollment.'
    $null = Invoke-Api -Method PUT -Path "/api/enrollments/$($selfEnrollment.id)" `
        -Body @{ status = 'Completed'; progress = 100; score = 88 }
    $null = Invoke-Api -Method POST -Path "/api/enrollments/$($selfEnrollment.id)/cancel" `
        -BearerToken $employeeToken -ExpectedStatus 409
    $null = Invoke-Api -Method PUT -Path "/api/enrollments/$($selfEnrollment.id)" `
        -Body @{ status = 'InProgress'; progress = 40; score = $null }
    $cancelledSelf = Invoke-Api -Method POST -Path "/api/enrollments/$($selfEnrollment.id)/cancel" `
        -BearerToken $employeeToken
    Assert-That ($cancelledSelf.status -eq 'Cancelled' -and $cancelledSelf.progress -eq 40 -and
        $null -eq $cancelledSelf.completedAt) 'Cancelling own in-progress enrollment preserves its history.'
    $courseAfterSelfCancel = Invoke-Api -Method GET -Path "/api/courses/$($selfCourse.id)"
    Assert-That ($courseAfterSelfCancel.enrolledCount -eq 0) 'Self-cancellation releases the course seat.'
    $null = Invoke-Api -Method POST -Path "/api/enrollments/$($selfEnrollment.id)/cancel" `
        -BearerToken $employeeToken -ExpectedStatus 409
    $rejoinedSelf = Invoke-Api -Method POST -Path '/api/enrollments/self' `
        -Body @{ courseId = $selfCourse.id } -BearerToken $employeeToken -ExpectedStatus 201
    Assert-That ($rejoinedSelf.id -eq $selfEnrollment.id -and $rejoinedSelf.status -eq 'Registered' -and
        $rejoinedSelf.progress -eq 0 -and $null -eq $rejoinedSelf.score) `
        'The same employee can register again after cancelling without losing the record identity.'
    $null = Invoke-Api -Method DELETE -Path "/api/employees/$($newEmployee.id)" -ExpectedStatus 409
    $cancelledRegistered = Invoke-Api -Method POST -Path "/api/enrollments/$($selfEnrollment.id)/cancel" `
        -BearerToken $employeeToken
    Assert-That ($cancelledRegistered.status -eq 'Cancelled' -and $cancelledRegistered.progress -eq 0) `
        'The employee can also cancel a newly registered course.'
    $null = Invoke-Api -Method POST -Path '/api/enrollments/2147483647/cancel' `
        -BearerToken $employeeToken -ExpectedStatus 404

    foreach ($employeeReadPath in @('/api/courses', "/api/courses/$($course.id)",
        '/api/employees', "/api/employees/$($employeeA.id)",
        '/api/enrollments', "/api/enrollments/$($enrollmentA.id)", '/api/dashboard')) {
        $null = Invoke-Api -Method GET -Path $employeeReadPath -BearerToken $employeeToken
    }
    $employeeWriteCases = @(
        @{ path = '/api/courses'; id = $course.id; body = $courseBody },
        @{ path = '/api/employees'; id = $employeeA.id; body = $employeeBodyA },
        @{ path = '/api/enrollments'; id = $enrollmentA.id; body = @{
            courseId = $course.id; employeeId = $employeeA.id
        } }
    )
    foreach ($writeCase in $employeeWriteCases) {
        $null = Invoke-Api -Method POST -Path $writeCase.path -Body $writeCase.body `
            -BearerToken $employeeToken -ExpectedStatus 403
        $updateBody = if ($writeCase.path -eq '/api/enrollments') {
            @{ status = 'InProgress'; progress = 80; score = $null }
        }
        else { $writeCase.body }
        $null = Invoke-Api -Method PUT -Path "$($writeCase.path)/$($writeCase.id)" -Body $updateBody `
            -BearerToken $employeeToken -ExpectedStatus 403
        $null = Invoke-Api -Method DELETE -Path "$($writeCase.path)/$($writeCase.id)" `
            -BearerToken $employeeToken -ExpectedStatus 403
    }
    $unchangedAfterForbiddenWrites = Invoke-Api -Method GET -Path "/api/enrollments/$($enrollmentA.id)"
    Assert-That ($unchangedAfterForbiddenWrites.status -eq 'InProgress' -and
        $unchangedAfterForbiddenWrites.progress -eq 80) 'Forbidden employee writes preserve existing training data.'
    $null = Invoke-Api -Method POST -Path '/api/auth/logout' -BearerToken $employeeToken -ExpectedStatus 204
    $null = Invoke-Api -Method GET -Path '/api/auth/me' -BearerToken $employeeToken -ExpectedStatus 401
    $null = Invoke-Api -Method GET -Path '/api/dashboard' -BearerToken $employeeToken -ExpectedStatus 401
    $null = Invoke-Api -Method GET -Path '/api/dashboard'

    $null = Invoke-Api -Method DELETE -Path "/api/employees/$($inactiveEmployee.id)" -ExpectedStatus 204
    $null = Invoke-Api -Method GET -Path "/api/employees/$($inactiveEmployee.id)" -ExpectedStatus 404
    $null = Invoke-Api -Method DELETE -Path "/api/courses/$($draftCourse.id)" -ExpectedStatus 204
    $null = Invoke-Api -Method GET -Path "/api/courses/$($draftCourse.id)" -ExpectedStatus 404

    # A second session demonstrates that logout invalidates all previous tokens
    # for this user, rather than only removing a token from the browser.
    $secondLogin = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        username = $adminUsername; password = $adminPassword
    }
    $secondToken = $secondLogin.accessToken
    Assert-That ($secondToken -ne $accessToken) 'Separate logins issue distinct JWTs.'
    $null = Invoke-Api -Method POST -Path '/api/auth/logout' -ExpectedStatus 204
    $null = Invoke-Api -Method GET -Path '/api/dashboard' -ExpectedStatus 401
    $null = Invoke-Api -Method GET -Path '/api/auth/me' -BearerToken $secondToken -ExpectedStatus 401
    $httpClient.DefaultRequestHeaders.Authorization = $null
    $relogin = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        username = $adminUsername; password = $adminPassword
    }
    $accessToken = $relogin.accessToken
    $newClaims = Read-JwtClaims $accessToken
    Assert-That ([int]$newClaims.token_version -eq [int]$claims.token_version + 1) `
        'A new login carries the token version incremented by logout.'
    $httpClient.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new(
        'Bearer', $accessToken)
    $newCurrentUser = Invoke-Api -Method GET -Path '/api/auth/me'
    Assert-That ($newCurrentUser.id -eq $login.user.id) 'The administrator can authenticate again after logout.'
    $null = Invoke-Api -Method GET -Path '/api/dashboard'

    $passed = $true
}
catch {
    Write-Host (Protect-DiagnosticText "FAIL: $($_.Exception.Message)") -ForegroundColor Red
    if ($databaseCreatedByThisRun) { Write-Host "Preserved SQL Server test database: $databaseName" }
    if (Test-Path -LiteralPath $runDirectory -PathType Container) {
        Write-Host "Test artifacts: $runDirectory"
        foreach ($logName in @('api.stdout.log', 'api.stderr.log')) {
            $logPath = Join-Path $runDirectory $logName
            if (Test-Path -LiteralPath $logPath -PathType Leaf) {
                Write-Host "Last API log lines ($logName):"
                Get-Content -LiteralPath $logPath -Tail 20 | ForEach-Object {
                    Write-Host (Protect-DiagnosticText $_)
                }
            }
        }
    }
}
finally {
    if ($null -ne $httpClient) { $httpClient.Dispose() }
    if ($null -ne $apiProcess) {
        $apiProcess.Refresh()
        if (-not $apiProcess.HasExited) {
            # Only this run's directly launched process is stopped.
            Stop-Process -InputObject $apiProcess -Force
            $apiProcess.WaitForExit()
        }
        $apiProcess.Dispose()
    }

    if ($passed -and -not $KeepArtifacts -and $databaseCreatedByThisRun) {
        if ($databaseName -notmatch '^TrainingManagement_Test_[a-f0-9]{32}$' -or
            $databaseName -ne "TrainingManagement_Test_$runId") {
            throw 'Refusing to drop a database outside the owned test database name.'
        }
        try {
            $null = Invoke-TestSql (
                "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                "DROP DATABASE [$databaseName];")
            $databaseCreatedByThisRun = $false
        }
        catch {
            $passed = $false
            Write-Host (Protect-DiagnosticText "FAIL: Test database cleanup failed: $($_.Exception.Message)") `
                -ForegroundColor Red
            Write-Host "Preserved SQL Server test database: $databaseName"
        }
    }

    if ($passed -and -not $KeepArtifacts -and (Test-Path -LiteralPath $runDirectory)) {
        $resolvedRunDirectory = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $runDirectory).Path)
        $allowedPrefix = $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedRunDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path $resolvedRunDirectory -Leaf) -ne $runId) {
            throw "Refusing cleanup outside the verified test directory: $resolvedRunDirectory"
        }
        Remove-Item -LiteralPath $resolvedRunDirectory -Recurse -Force
    }
    elseif ($passed -and $KeepArtifacts) {
        Write-Host "Test artifacts: $runDirectory"
        Write-Host "Preserved SQL Server test database: $databaseName"
    }
}

if ($passed) {
    Write-Host "PASS: $assertionCount HTTP, JWT, SQL Server, and data assertions succeeded." -ForegroundColor Green
    exit 0
}
exit 1
