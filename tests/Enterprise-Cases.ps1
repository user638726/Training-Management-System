# Runs in the isolated smoke-test process; never targets the daily database.
Write-Host 'Checking enterprise governance, learning workflows, imports and reports...'
function Set-EnterpriseAccount {
    param($Account, [string]$Role, [Nullable[int]]$EmployeeId = $null)
    $body = @{ role = $Role; isActive = $true; isApproved = $true; employeeId = $EmployeeId; rowVersion = $Account.rowVersion }
    return Invoke-Api -Method PUT -Path "/api/enterprise/accounts/$($Account.id)" -Body $body
}
function New-EnterpriseUser {
    param([string]$Suffix, [string]$Role, $Employee)
    $body = New-RegistrationBody -Suffix $Suffix
    $registered = Invoke-Api -Method POST -Path '/api/auth/register' -Body $body -ExpectedStatus 201
    $account = @(Invoke-Api -Method GET -Path '/api/enterprise/accounts') | Where-Object id -eq $registered.id
    $null = Set-EnterpriseAccount $account $Role $Employee.id
    $login = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = $body.username; password = $registeredPassword }
    return @{ id = $registered.id; token = $login.accessToken; employeeId = $Employee.id }
}
$employeeBody1 = New-EmployeeBody 'ent1'; $employeeBody1.department = 'Enterprise Alpha'
$employeeBody2 = New-EmployeeBody 'ent2'; $employeeBody2.department = 'Enterprise Beta'
$enterpriseEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body $employeeBody1 -ExpectedStatus 201
$outsideEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body $employeeBody2 -ExpectedStatus 201
$managerEmployeeBody = New-EmployeeBody 'entmgr'; $managerEmployeeBody.department = 'Enterprise Alpha'
$managerEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body $managerEmployeeBody -ExpectedStatus 201
$teacherEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body (New-EmployeeBody 'entteach') -ExpectedStatus 201
$learner = New-EnterpriseUser 'enterprise-learner' 'Employee' $enterpriseEmployee
$manager = New-EnterpriseUser 'enterprise-manager' 'Manager' $managerEmployee
$teacher = New-EnterpriseUser 'enterprise-teacher' 'Instructor' $teacherEmployee
$null = Invoke-Api -Method POST -Path '/api/enterprise/departments' -Body @{ name = 'Enterprise Alpha'; managerUserId = $manager.id } -ExpectedStatus 201
$departments = @(Invoke-Api -Method GET -Path '/api/enterprise/departments')
$alpha = $departments | Where-Object name -eq 'Enterprise Alpha'
$null = Invoke-Api -Method POST -Path '/api/enterprise/departments' -Body @{ name = 'Enterprise Beta' } -ExpectedStatus 201
$beta = @(Invoke-Api -Method GET -Path '/api/enterprise/departments') | Where-Object name -eq 'Enterprise Beta'
$null = Invoke-Api -Method PUT -Path "/api/enterprise/departments/$($alpha.id)" -Body @{ name = $alpha.name; parentId = $alpha.id; rowVersion = $alpha.rowVersion } -ExpectedStatus 400
$managerEmployees = @(Invoke-Api -Method GET -Path '/api/employees' -BearerToken $manager.token)
Assert-That ($managerEmployees.Count -eq 2) 'Managers see employees in their department only.'
$null = Invoke-Api -Method GET -Path "/api/employees/$($outsideEmployee.id)" -BearerToken $manager.token -ExpectedStatus 404
$null = Invoke-Api -Method GET -Path '/api/enterprise/audit' -BearerToken $manager.token -ExpectedStatus 403
$null = Invoke-Api -Method GET -Path '/api/enterprise/accounts' -BearerToken $learner.token -ExpectedStatus 403
$enterpriseCourseBody = New-CourseBody -Capacity 1; $enterpriseCourseBody.title = 'Enterprise evidence course'
$enterpriseCourse = Invoke-Api -Method POST -Path '/api/courses' -Body $enterpriseCourseBody -ExpectedStatus 201
$null = Invoke-Api -Method PUT -Path "/api/enterprise/courses/$($enterpriseCourse.id)/settings" -Body @{
    requiresApproval = $true; requiresAttendance = $true; requiresQuiz = $true; passScore = 80; cost = 1200; certificateValidityMonths = 1
    instructorUserId = $teacher.id; rowVersion = $enterpriseCourse.rowVersion
} -ExpectedStatus 204
$null = Invoke-Api -Method PUT -Path "/api/enterprise/courses/$($enterpriseCourse.id)/settings" -Body @{
    rowVersion = $enterpriseCourse.rowVersion; cost = 1
} -ExpectedStatus 409
$enterpriseCourse = Invoke-Api -Method GET -Path "/api/courses/$($enterpriseCourse.id)"
$null = Invoke-Api -Method PUT -Path "/api/enterprise/courses/$($enterpriseCourse.id)/quiz" -Body @{
    rowVersion = $enterpriseCourse.rowVersion; questions = @(@{ prompt = 'Which option is correct?'; options = @('Correct', 'Wrong'); correctOption = 0 })
} -BearerToken $teacher.token -ExpectedStatus 204
$null = Invoke-Api -Method POST -Path '/api/enrollments/self' -Body @{ courseId = $enterpriseCourse.id } -BearerToken $learner.token -ExpectedStatus 409
$approval = Invoke-Api -Method POST -Path '/api/enterprise/approvals' -Body @{ courseId = $enterpriseCourse.id } -BearerToken $learner.token -ExpectedStatus 201
$null = Invoke-Api -Method POST -Path '/api/enterprise/approvals' -Body @{ courseId = $enterpriseCourse.id } -BearerToken $learner.token -ExpectedStatus 409
$pending = @(Invoke-Api -Method GET -Path '/api/enterprise/approvals' -BearerToken $manager.token) | Where-Object id -eq $approval.id
Assert-That ($pending.status -eq 'Pending') 'Department managers receive pending enrollment requests.'
$null = Invoke-Api -Method POST -Path "/api/enterprise/approvals/$($approval.id)/decision" -Body @{ approve = $true; rowVersion = $pending.rowVersion } -BearerToken $learner.token -ExpectedStatus 403
$null = Invoke-Api -Method POST -Path "/api/enterprise/approvals/$($approval.id)/decision" -Body @{ approve = $true; rowVersion = $pending.rowVersion; comment = 'Approved for training' } -BearerToken $manager.token -ExpectedStatus 204
$null = Invoke-Api -Method POST -Path "/api/enterprise/approvals/$($approval.id)/decision" -Body @{ approve = $true; rowVersion = $pending.rowVersion } -BearerToken $manager.token -ExpectedStatus 409
$enterpriseEnrollment = @(Invoke-Api -Method GET -Path '/api/enrollments/mine' -BearerToken $learner.token) | Where-Object courseId -eq $enterpriseCourse.id
$plan = Invoke-Api -Method POST -Path '/api/enterprise/plans' -Body @{ title = 'Mandatory Alpha training'; department = 'Enterprise Alpha'; dueDays = 1; recurrenceMonths = 12; courseIds = @($enterpriseCourse.id); isActive = $true } -ExpectedStatus 201
$assigned = Invoke-Api -Method POST -Path "/api/enterprise/plans/$($plan.id)/assign"
Assert-That ($assigned.assigned -eq 2) 'Plans assign all matching employees.'
$assignedAgain = Invoke-Api -Method POST -Path "/api/enterprise/plans/$($plan.id)/assign"
Assert-That ($assignedAgain.assigned -eq 0) 'Assignment is idempotent within the recurrence cycle.'
$assignments = @(Invoke-Api -Method GET -Path '/api/enterprise/assignments' -BearerToken $learner.token)
Assert-That ($assignments.Count -eq 1 -and $assignments[0].employeeId -eq $enterpriseEmployee.id) 'Learners only see their own mandatory assignments.'
$quiz = Invoke-Api -Method GET -Path "/api/enterprise/courses/$($enterpriseCourse.id)/quiz" -BearerToken $learner.token
Assert-That ($null -eq $quiz.questions[0].correctOption) 'Quiz answers are withheld from learners.'
$answers = @{}; $answers[([string]$quiz.questions[0].id)] = 0
$quizResult = Invoke-Api -Method POST -Path "/api/enterprise/enrollments/$($enterpriseEnrollment.id)/quiz" -Body @{ revision = $quiz.revision; answers = $answers } -BearerToken $learner.token
Assert-That ($quizResult.score -eq 100 -and $quizResult.status -eq 'InProgress') 'Passing quiz waits for required attendance.'
$null = Invoke-Api -Method PUT -Path "/api/enrollments/$($enterpriseEnrollment.id)" -Body @{ status = 'Completed'; progress = 100; score = 100 } -ExpectedStatus 409
$enterpriseEnrollment = Invoke-Api -Method GET -Path "/api/enrollments/$($enterpriseEnrollment.id)"
$null = Invoke-Api -Method POST -Path "/api/enterprise/enrollments/$($enterpriseEnrollment.id)/attendance" -Body @{ attended = $true; rowVersion = $enterpriseEnrollment.rowVersion } -BearerToken $teacher.token -ExpectedStatus 204
$completedEnterprise = Invoke-Api -Method GET -Path "/api/enrollments/$($enterpriseEnrollment.id)"
Assert-That ($completedEnterprise.status -eq 'Completed' -and $completedEnterprise.recognizedHours -eq 3.5) 'Attendance and passed quiz complete training with immutable recognized hours.'
$certificates = @(Invoke-Api -Method GET -Path '/api/enterprise/certificates' -BearerToken $learner.token)
Assert-That ($certificates.Count -eq 1 -and $null -ne $certificates[0].expiresAt) 'Completion issues a scoped certificate with expiration.'
$assignments = @(Invoke-Api -Method GET -Path '/api/enterprise/assignments' -BearerToken $learner.token)
Assert-That ($null -ne $assignments[0].completedAt) 'Completion satisfies the mandatory assignment.'
$null = Invoke-Api -Method POST -Path "/api/enterprise/enrollments/$($enterpriseEnrollment.id)/survey" -Body @{ rating = 5; feedback = 'Useful training' } -BearerToken $learner.token -ExpectedStatus 201
$null = Invoke-Api -Method POST -Path "/api/enterprise/enrollments/$($enterpriseEnrollment.id)/survey" -Body @{ rating = 5 } -BearerToken $learner.token -ExpectedStatus 409
$enterpriseCourse = Invoke-Api -Method GET -Path "/api/courses/$($enterpriseCourse.id)"
$enterpriseCourseBody.durationHours = 8; $enterpriseCourseBody.rowVersion = $enterpriseCourse.rowVersion
$null = Invoke-Api -Method PUT -Path "/api/courses/$($enterpriseCourse.id)" -Body $enterpriseCourseBody
$historical = Invoke-Api -Method GET -Path "/api/enrollments/$($enterpriseEnrollment.id)"
Assert-That ($historical.recognizedHours -eq 3.5) 'Editing current course hours does not rewrite recognized history.'
$report = Invoke-Api -Method GET -Path '/api/enterprise/reports' -BearerToken $manager.token
Assert-That ($report.completed -eq 1 -and $report.hours -eq 3.5 -and $report.cost -eq 1200 -and $report.satisfaction -eq 5) 'Management reports aggregate scoped evidence and original costs.'
$null = Invoke-Api -Method POST -Path '/api/enterprise/report-schedules' -Body @{ title = 'Alpha weekly report'; frequency = 'Weekly' } -BearerToken $manager.token -ExpectedStatus 201
$null = Invoke-Api -Method POST -Path '/api/enterprise/operations/run'
$deliveries = @(Invoke-Api -Method GET -Path '/api/enterprise/report-deliveries' -BearerToken $manager.token)
Assert-That ($deliveries.Count -eq 1) 'Scheduled reporting produces a downloadable snapshot.'
$notifications = @(Invoke-Api -Method GET -Path '/api/enterprise/notifications' -BearerToken $learner.token)
Assert-That ($notifications.Count -ge 3) 'Learning events produce notifications.'
$null = Invoke-Api -Method POST -Path "/api/enterprise/notifications/$($notifications[0].id)/read" -BearerToken $manager.token -ExpectedStatus 404
$null = Invoke-Api -Method POST -Path "/api/enterprise/notifications/$($notifications[0].id)/read" -BearerToken $learner.token -ExpectedStatus 204
$null = Invoke-Api -Method PUT -Path "/api/enrollments/$($enterpriseEnrollment.id)" -Body @{ status = 'InProgress'; progress = 50; score = 100 }
$revoked = @(Invoke-Api -Method GET -Path '/api/enterprise/certificates' -BearerToken $learner.token)
Assert-That ($null -ne $revoked[0].revokedAt) 'Reopening completion revokes the prior certificate.'
$history = @(Invoke-Api -Method GET -Path "/api/enterprise/enrollments/$($enterpriseEnrollment.id)/history" -BearerToken $learner.token)
Assert-That ($history.Count -eq 1) 'Reopening preserves the original completion evidence.'
$null = Invoke-Api -Method DELETE -Path "/api/enrollments/$($enterpriseEnrollment.id)" -ExpectedStatus 409
$csvHeader = 'employeeNumber,name,email,department,jobTitle,isActive,hiredAt'
$csv = $csvHeader + "`r`n" + "IMP-$runToken,`"Imported, User`",import-$runToken@example.test,Enterprise Beta,Specialist,true,2026-01-01"
$preview = Invoke-Api -Method POST -Path '/api/enterprise/imports/preview' -Body @{ csv = $csv }
Assert-That ($preview.valid -and $preview.rows[0].employee.name -eq 'Imported, User') 'CSV preview supports quoted commas and validates rows.'
$imported = Invoke-Api -Method POST -Path "/api/enterprise/imports/$($preview.id)/commit"
Assert-That ($imported.created -eq 1) 'Validated import commits new personnel records.'
$null = Invoke-Api -Method POST -Path "/api/enterprise/imports/$($preview.id)/commit" -ExpectedStatus 409
$badPreview = Invoke-Api -Method POST -Path '/api/enterprise/imports/preview' -Body @{ csv = ($csv + "`r`n" + "IMP-$runToken,Duplicate,invalid,Enterprise Beta,Specialist,true,") }
Assert-That (-not $badPreview.valid) 'CSV preview blocks duplicate identifiers and malformed emails.'
$null = Invoke-Api -Method POST -Path "/api/enterprise/imports/$($badPreview.id)/commit" -ExpectedStatus 400
$null = Invoke-Api -Method POST -Path '/api/enterprise/imports/hr-preview' -ExpectedStatus 409
$null = Invoke-Api -Method GET -Path '/api/enterprise/integrations' -BearerToken $manager.token -ExpectedStatus 403
$integration = Invoke-Api -Method GET -Path '/api/enterprise/integrations'
Assert-That (-not $integration.sso.enabled -and -not $integration.mail.enabled) 'External integrations remain disabled without configuration.'
$null = Invoke-Api -Method GET -Path '/api/auth/sso/start' -BearerToken '' -ExpectedStatus 409
$audit = Invoke-Api -Method GET -Path '/api/enterprise/audit?entity=Enrollment'
Assert-That ($audit.total -gt 0) 'Training mutations produce append-only audit records.'
$userAudit = Invoke-Api -Method GET -Path '/api/enterprise/audit?entity=AppUser'
Assert-That (($userAudit | ConvertTo-Json -Depth 20) -notmatch 'PasswordHash|TokenVersion') 'Account audit records exclude credential material.'
$inviteResult = Invoke-Api -Method POST -Path '/api/enterprise/invitations' -Body @{ email = "invite-$runToken@example.test"; role = 'Employee' }
$inviteBody = @{ token = $inviteResult.token; username = "invite-$runToken"; displayName = 'Invited colleague'; password = $registeredPassword }
$acceptedInvite = Invoke-Api -Method POST -Path '/api/enterprise/invitations/accept' -Body $inviteBody -BearerToken '' -ExpectedStatus 201
Assert-That ($acceptedInvite.isApproved) 'Company invitations create approved accounts.'
$null = Invoke-Api -Method POST -Path '/api/enterprise/invitations/accept' -Body $inviteBody -BearerToken '' -ExpectedStatus 400
$account = @(Invoke-Api -Method GET -Path '/api/enterprise/accounts') | Where-Object id -eq $learner.id
$null = Invoke-Api -Method PUT -Path "/api/enterprise/accounts/$($learner.id)" -Body @{ role = 'Employee'; employeeId = $enterpriseEmployee.id; isActive = $false; isApproved = $true; rowVersion = $account.rowVersion }
$null = Invoke-Api -Method GET -Path '/api/auth/me' -BearerToken $learner.token -ExpectedStatus 401

# Boundary checks for paging, file evidence, exports and registration approval.
$pageHeaders = $null
$pageRows = @(Invoke-Api -Method GET -Path '/api/employees?page=1&pageSize=1&department=Enterprise%20Alpha' -BearerToken $manager.token -ResponseHeaders ([ref]$pageHeaders))
Assert-That ($pageRows.Count -eq 1 -and $pageHeaders['X-Total-Count'] -eq '2') 'Server paging reports a scoped total without loading the entire list.'
$null = Invoke-Api -Method GET -Path '/api/courses?status=Invalid' -ExpectedStatus 400
function Invoke-EnterpriseRaw {
    param([string]$Method, [string]$Path, [string]$Token, [Net.Http.HttpContent]$Content = $null)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), "$baseUrl$Path")
    $request.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
    if ($null -ne $Content) { $request.Content = $Content }
    try {
        $response = $httpClient.SendAsync($request).GetAwaiter().GetResult()
        try { return @{ status = [int]$response.StatusCode; bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() } }
        finally { $response.Dispose() }
    } finally { $request.Dispose() }
}
$multipart = [Net.Http.MultipartFormDataContent]::new()
$fileContent = [Net.Http.ByteArrayContent]::new([Text.Encoding]::UTF8.GetBytes('Training evidence'))
$fileContent.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('text/plain')
$multipart.Add($fileContent, 'file', 'lesson.txt')
$upload = Invoke-EnterpriseRaw 'POST' "/api/enterprise/courses/$($enterpriseCourse.id)/materials" $teacher.token $multipart
Assert-That ($upload.status -eq 201) 'Assigned instructors can upload validated teaching material.'
$material = [Text.Encoding]::UTF8.GetString($upload.bytes) | ConvertFrom-Json
$download = Invoke-EnterpriseRaw 'GET' "/api/enterprise/materials/$($material.id)/download" $manager.token
Assert-That ($download.status -eq 200 -and [Text.Encoding]::UTF8.GetString($download.bytes) -eq 'Training evidence') 'Authorized users can download stored material.'
$reportExport = Invoke-EnterpriseRaw 'GET' '/api/enterprise/reports/export' $manager.token
Assert-That ($reportExport.status -eq 200 -and $reportExport.bytes[0] -eq 239 -and $reportExport.bytes[1] -eq 187) 'Report exports contain a UTF-8 BOM for Excel.'
$reportText = [Text.Encoding]::UTF8.GetString($reportExport.bytes)
Assert-That (-not $reportText.Contains($outsideEmployee.name)) 'Report exports exclude employees outside the manager scope.'
$materialQuiz = Invoke-Api -Method GET -Path "/api/enterprise/courses/$($enterpriseCourse.id)/quiz" -BearerToken $teacher.token
Assert-That ($materialQuiz.questions.Count -eq 1) 'Changing course metadata preserves the active quiz version.'
$staleCsv = $csvHeader + "`r`n" + "$($outsideEmployee.employeeNumber),Updated Name,$($outsideEmployee.email),Enterprise Beta,Specialist,true,2026-01-01"
$stalePreview = Invoke-Api -Method POST -Path '/api/enterprise/imports/preview' -Body @{ csv = $staleCsv }
$outsideBody = $employeeBody2.Clone(); $outsideBody.name = 'Concurrent HR edit'; $outsideBody.rowVersion = $outsideEmployee.rowVersion
$null = Invoke-Api -Method PUT -Path "/api/employees/$($outsideEmployee.id)" -Body $outsideBody
$null = Invoke-Api -Method POST -Path "/api/enterprise/imports/$($stalePreview.id)/commit" -ExpectedStatus 409

Stop-TestApiProcess
$processConfiguration['Enterprise__RequireRegistrationApproval'] = 'true'
$apiProcess = Start-TestApiProcess -Configuration $processConfiguration -StandardOutput $stdoutPath -StandardError $stderrPath
Wait-TestApiReady
$pendingRegistration = New-RegistrationBody -Suffix 'awaiting-approval'
$pendingAccount = Invoke-Api -Method POST -Path '/api/auth/register' -Body $pendingRegistration -BearerToken '' -ExpectedStatus 201
Assert-That (-not $pendingAccount.isApproved) 'Enterprise registration requires administrator approval by default.'
$null = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = $pendingRegistration.username; password = $registeredPassword } -BearerToken '' -ExpectedStatus 401
$pendingAccount = @(Invoke-Api -Method GET -Path '/api/enterprise/accounts') | Where-Object id -eq $pendingAccount.id
$null = Set-EnterpriseAccount $pendingAccount 'Employee'
$approvedLogin = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = $pendingRegistration.username; password = $registeredPassword } -BearerToken ''
Assert-That ($approvedLogin.user.isApproved) 'Approved accounts can subsequently sign in.'
$hrEmployee = Invoke-Api -Method POST -Path '/api/employees' -Body (New-EmployeeBody 'enthr') -ExpectedStatus 201
$hr = New-EnterpriseUser 'enterprise-hr' 'HR' $hrEmployee
$null = Invoke-Api -Method GET -Path '/api/enterprise/staff-directory' -BearerToken $hr.token
$null = Invoke-Api -Method GET -Path '/api/enterprise/accounts' -BearerToken $hr.token -ExpectedStatus 403
$hrCourse = Invoke-Api -Method POST -Path '/api/courses' -Body (New-CourseBody -Status 'Draft') -BearerToken $hr.token -ExpectedStatus 201
Assert-That ($hrCourse.status -eq 'Draft') 'HR can manage training while account permissions remain Admin-only.'
$teacherAccount = @(Invoke-Api -Method GET -Path '/api/enterprise/accounts') | Where-Object id -eq $teacher.id
$null = Set-EnterpriseAccount $teacherAccount 'Employee' $teacherEmployee.id
$teacherLogin = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ username = "enterprise-teacher-$runToken"; password = $registeredPassword } -BearerToken ''
$formerTeacherQuiz = Invoke-Api -Method GET -Path "/api/enterprise/courses/$($enterpriseCourse.id)/quiz" -BearerToken $teacherLogin.accessToken
Assert-That ($null -eq $formerTeacherQuiz.questions[0].correctOption) 'Revoking instructor role also revokes access to quiz answer keys.'
$null = Invoke-Api -Method PUT -Path "/api/enterprise/departments/$($beta.id)" -Body @{ name = $beta.name; parentId = $alpha.id; rowVersion = $beta.rowVersion } -ExpectedStatus 204
$hierarchyEmployees = @(Invoke-Api -Method GET -Path '/api/employees' -BearerToken $manager.token)
Assert-That ($hierarchyEmployees.Count -eq 4) 'Department scope includes descendant departments and their imported staff.'
$oldDelivery = Invoke-EnterpriseRaw 'GET' "/api/enterprise/report-deliveries/$($deliveries[0].id)/download" $manager.token
Assert-That ($oldDelivery.status -eq 403) 'Changed department scope invalidates older report snapshots.'
