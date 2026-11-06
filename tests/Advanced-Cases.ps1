Write-Host 'Checking all eight advanced business modules...'
$stubProcess=$null
function Invoke-Advanced {
    param([string]$Method,[string]$Path,[object]$Body=$null,[string]$Token=$script:accessToken,[int]$Expected=200,[Net.Http.HttpContent]$Content=$null,[switch]$Xapi)
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),"$baseUrl$Path")
    $request.Headers.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$Token)
    if($Xapi){$request.Headers.Add('X-Experience-API-Version','1.0.3')}
    if($Content){$request.Content=$Content}elseif($null -ne $Body){$request.Content=[Net.Http.StringContent]::new((ConvertTo-Json -InputObject $Body -Depth 30 -Compress),[Text.Encoding]::UTF8,'application/json')}
    try{$response=$httpClient.SendAsync($request).GetAwaiter().GetResult();try{$text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();if([int]$response.StatusCode -ne $Expected){throw "$Method $Path expected $Expected, got $([int]$response.StatusCode): $(Protect-DiagnosticText $text)"};$script:assertionCount++;if($text.Trim()){return ConvertFrom-Json -InputObject $text}}finally{$response.Dispose()}}finally{$request.Dispose()}
}
function New-EvidenceForm {
    param([hashtable]$Values,[string]$Field='file',[string]$FileName='proof.pdf',[string]$Text="%PDF-1.4`nTest-only fixture")
    $form=[Net.Http.MultipartFormDataContent]::new();foreach($key in $Values.Keys){if($null -ne $Values[$key]){$form.Add([Net.Http.StringContent]::new([string]$Values[$key]),$key)}}
    $part=[Net.Http.ByteArrayContent]::new([Text.Encoding]::UTF8.GetBytes($Text));$part.Headers.ContentType=[Net.Http.Headers.MediaTypeHeaderValue]::new('application/pdf');$form.Add($part,$Field,$FileName);return ,$form
}
function Advance-Flow {
    param([int]$Id,[string]$Token,[bool]$Approve=$true)
    $flow=@(Invoke-Api -Method GET -Path '/api/advanced/workflows' -BearerToken $Token)|Where-Object id -eq $Id
    return Invoke-Api -Method POST -Path "/api/advanced/workflows/$Id/decision" -BearerToken $Token -Body @{approve=$Approve;rowVersion=$flow.rowVersion;comment='Integration approval'}
}
try {
    $stubDir=Join-Path $runDirectory 'integration-stub';New-Item -ItemType Directory -Path $stubDir -Force|Out-Null
    'employeeNumber,name,email,department,jobTitle,isActive'|Set-Content (Join-Path $stubDir 'employees.csv') -Encoding UTF8
    '{"failures":1}'|Set-Content (Join-Path $stubDir 'response.json') -Encoding ASCII
    $stubProcess=Start-Process node -ArgumentList @(('"'+(Join-Path $PSScriptRoot 'integration-stub.cjs')+'"'),('"'+$stubDir+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stubDir 'stdout.log') -RedirectStandardError (Join-Path $stubDir 'stderr.log')
    for($attempt=0;$attempt -lt 60 -and -not(Test-Path (Join-Path $stubDir 'ready.json'));$attempt++){if($stubProcess.HasExited){throw 'Loopback integration stub exited.'};Start-Sleep -Milliseconds 100}
    $stubPort=(Get-Content (Join-Path $stubDir 'ready.json') -Raw|ConvertFrom-Json).port
    Stop-TestApiProcess
    $processConfiguration['Integrations__AllowLoopbackWebhooks']='true';$processConfiguration['Hr__EmployeesEndpoint']="http://127.0.0.1:$stubPort/employees.csv"
    $apiProcess=Start-TestApiProcess -Configuration $processConfiguration -StandardOutput $stdoutPath -StandardError $stderrPath;Wait-TestApiReady

    $account=@(Invoke-Api -Method GET -Path '/api/enterprise/accounts')|Where-Object id -eq $learner.id
    $null=Set-EnterpriseAccount $account 'Employee' $enterpriseEmployee.id
    $learnerLogin=Invoke-Api -Method POST -Path '/api/auth/login' -Body @{username="enterprise-learner-$runToken";password=$registeredPassword}
    $learner.token=$learnerLogin.accessToken
    foreach($lookupKind in @('courses','employees','users','departments','skills','credentialTypes','series','budgets','policies','clients','activities')){
        $lookup=Invoke-Api -Method GET -Path "/api/advanced/lookups?kind=$lookupKind&page=1"
        Assert-That ($lookup.total -ge 0) "Remote lookup $lookupKind supports paged server queries."
    }
    $proxyBody=New-EmployeeBody 'proxy';$proxyBody.department='Enterprise Alpha'
    $proxyEmployee=Invoke-Api -Method POST -Path '/api/employees' -Body $proxyBody -ExpectedStatus 201
    $proxy=New-EnterpriseUser 'proxy' 'Employee' $proxyEmployee
    $policy=Invoke-Api -Method POST -Path '/api/advanced/policies' -ExpectedStatus 201 -Body @{name='Three-stage training';minimumAmount=500;stages=@(@{name='Manager';kind='Manager';dueHours=24},@{name='HR';kind='HR';dueHours=24},@{name='Budget';kind='BudgetOwner';dueHours=24});isActive=$true}
    $budget=Invoke-Api -Method POST -Path '/api/advanced/budgets' -ExpectedStatus 201 -Body @{name='Alpha annual budget';year=[DateTime]::UtcNow.AddHours(8).Year;department='Enterprise Alpha';ownerUserId=$hr.id;allocated=3000;currency='TWD';expensePolicyId=$policy.id;isActive=$true}
    $body=New-CourseBody -Capacity 2;$body.title='Advanced approval and budget'
    $advancedCourse=Invoke-Api -Method POST -Path '/api/courses' -Body $body -ExpectedStatus 201
    $null=Invoke-Api -Method PUT -Path "/api/enterprise/courses/$($advancedCourse.id)/settings" -Body @{requiresApproval=$true;cost=1000;rowVersion=$advancedCourse.rowVersion} -ExpectedStatus 204
    $advancedCourse=Invoke-Api -Method GET -Path "/api/courses/$($advancedCourse.id)"
    $null=Invoke-Api -Method PUT -Path "/api/advanced/courses/$($advancedCourse.id)/advanced" -Body @{policyId=$policy.id;budgetId=$budget.id;rowVersion=$advancedCourse.rowVersion} -ExpectedStatus 204
    $request=Invoke-Api -Method POST -Path '/api/enterprise/approvals' -Body @{courseId=$advancedCourse.id} -BearerToken $learner.token -ExpectedStatus 201
    $flow=@(Invoke-Api -Method GET -Path '/api/advanced/workflows' -BearerToken $manager.token)|Where-Object title -eq $body.title
    Assert-That ($flow.stages.Count -eq 3 -and $flow.canAct) 'Three-stage workflow snapshots its reviewer sequence.'
    $null=Invoke-Api -Method POST -Path "/api/advanced/workflows/$($flow.id)/decision" -Body @{approve=$true;rowVersion=$flow.rowVersion} -BearerToken $hr.token -ExpectedStatus 403
    $null=Invoke-Api -Method POST -Path '/api/advanced/delegations' -Body @{principalUserId=$manager.id;delegateUserId=$proxy.id;startsAt=[DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o');endsAt=[DateTimeOffset]::UtcNow.AddHours(2).ToString('o');reason='Temporary test coverage'} -BearerToken $manager.token -ExpectedStatus 201
    $null=Advance-Flow $flow.id $proxy.token
    $flowAfter=@(Invoke-Api -Method GET -Path '/api/advanced/workflows' -BearerToken $hr.token)|Where-Object id -eq $flow.id
    Assert-That ($flowAfter.stages[0].actorUserId -eq $proxy.id -and $flowAfter.stages[0].representedUserId -eq $manager.id -and $flowAfter.status -eq 'Pending') 'Delegated decisions record actual and represented reviewers and do not skip remaining stages.'
    $null=Advance-Flow $flow.id $hr.token;$null=Advance-Flow $flow.id $hr.token
    $advancedEnrollment=@(Invoke-Api -Method GET -Path '/api/enrollments/mine' -BearerToken $learner.token)|Where-Object courseId -eq $advancedCourse.id
    Assert-That ($advancedEnrollment.status -eq 'Registered') 'Final approval atomically registers the employee.'
    $budgetState=@(Invoke-Api -Method GET -Path '/api/advanced/budgets')|Where-Object id -eq $budget.id
    Assert-That ($budgetState.used -eq 1000 -and $budgetState.available -eq 2000) 'Registration reserves the course cost within the annual budget.'
    $null=Invoke-Api -Method PUT -Path "/api/enrollments/$($advancedEnrollment.id)" -Body @{status='Completed';progress=100;score=90}
    $expense=Invoke-Advanced -Method POST -Path '/api/advanced/expenses' -Token $learner.token -Expected 201 -Content (New-EvidenceForm @{budgetId=$budget.id;enrollmentId=$advancedEnrollment.id;title='External training fee';vendor='Test provider';amount=1200} 'receipt')
    $null=Advance-Flow $expense.workflowId $proxy.token;$null=Advance-Flow $expense.workflowId $hr.token;$null=Advance-Flow $expense.workflowId $hr.token
    $budgetState=@(Invoke-Api -Method GET -Path '/api/advanced/budgets')|Where-Object id -eq $budget.id
    Assert-That ($budgetState.used -eq 1200 -and $budgetState.available -eq 1800) 'Approved actual expense replaces its reservation without double-counting.'
    $claim=@(Invoke-Api -Method GET -Path '/api/advanced/expenses' -BearerToken $hr.token)|Where-Object id -eq $expense.id
    $null=Invoke-Api -Method POST -Path "/api/advanced/expenses/$($claim.id)/paid" -Body @{rowVersion=$claim.rowVersion} -BearerToken $hr.token -ExpectedStatus 204
    $null=Invoke-Api -Method PUT -Path "/api/advanced/budgets/$($budget.id)" -Body @{name=$budgetState.name;year=$budgetState.year;department=$budgetState.department;ownerUserId=$hr.id;allocated=100;currency='TWD';isActive=$true;rowVersion=$budgetState.rowVersion} -ExpectedStatus 409

    $template=Invoke-Api -Method POST -Path '/api/courses' -Body (New-CourseBody -Capacity 10) -ExpectedStatus 201
    $series=Invoke-Api -Method POST -Path '/api/advanced/series' -Body @{title='Repeated technical workshop'} -ExpectedStatus 201
    $classStart=[DateTimeOffset]::UtcNow.AddDays(40);$classEnd=$classStart.AddHours(4)
    $offering=Invoke-Api -Method POST -Path "/api/advanced/series/$($series.id)/offerings" -Body @{sourceCourseId=$template.id;title='Workshop class A';startsAt=$classStart.ToString('o');endsAt=$classEnd.ToString('o');capacity=1;location='Room A'} -ExpectedStatus 201
    $offering2=Invoke-Api -Method POST -Path "/api/advanced/series/$($series.id)/offerings" -Body @{sourceCourseId=$template.id;title='Workshop class B';startsAt=$classStart.ToString('o');endsAt=$classEnd.ToString('o');capacity=1;location='Room B'} -ExpectedStatus 201
    foreach($class in @($offering,$offering2)){$c=Invoke-Api -Method GET -Path "/api/courses/$($class.id)";$publish=New-CourseBody -Capacity 1;$publish.title=$class.title;$publish.startDate=$classStart.ToString('o');$publish.endDate=$classEnd.ToString('o');$publish.rowVersion=$c.rowVersion;$null=Invoke-Api -Method PUT -Path "/api/courses/$($class.id)" -Body $publish}
    $null=Invoke-Api -Method POST -Path "/api/advanced/courses/$($offering.id)/meetings" -Body @{startsAt=$classStart.AddMinutes(15).ToString('o');endsAt=$classEnd.AddMinutes(-15).ToString('o');location='Room A'} -ExpectedStatus 201
    $classEnrollment=Invoke-Api -Method POST -Path '/api/enrollments/self' -Body @{courseId=$offering.id} -BearerToken $learner.token -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path '/api/enrollments/self' -Body @{courseId=$offering2.id} -BearerToken $learner.token -ExpectedStatus 409
    $wait=Invoke-Api -Method POST -Path '/api/advanced/waitlist' -Body @{courseId=$offering.id} -BearerToken $manager.token -ExpectedStatus 201
    $proxyWait=Invoke-Api -Method POST -Path '/api/advanced/waitlist' -Body @{courseId=$offering.id} -BearerToken $proxy.token -ExpectedStatus 201
    $hrWait=Invoke-Api -Method POST -Path '/api/advanced/waitlist' -Body @{courseId=$offering.id} -BearerToken $hr.token -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path "/api/enrollments/$($classEnrollment.id)/cancel" -BearerToken $learner.token
    $waitState=@(Invoke-Api -Method GET -Path '/api/advanced/waitlist' -BearerToken $manager.token)|Where-Object id -eq $wait.id
    Assert-That ($waitState.status -eq 'Offered') 'Cancellation offers the seat to the first queued employee.'
    $null=Invoke-Api -Method POST -Path '/api/enrollments/self' -Body @{courseId=$offering.id} -BearerToken $proxy.token -ExpectedStatus 409
    $accepted=Invoke-Api -Method POST -Path "/api/advanced/waitlist/$($wait.id)/accept" -BearerToken $manager.token
    $null=Invoke-Api -Method POST -Path "/api/enrollments/$($accepted.enrollmentId)/cancel" -BearerToken $manager.token
    $null=Invoke-TestSql "UPDATE [$databaseName].dbo.Waitlist SET OfferExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=$($proxyWait.id);"
    $null=Invoke-Api -Method POST -Path '/api/advanced/jobs/run'
    $proxyState=@(Invoke-Api -Method GET -Path '/api/advanced/waitlist' -BearerToken $proxy.token)|Where-Object id -eq $proxyWait.id
    Assert-That ($proxyState.status -eq 'Expired') 'Unconfirmed offers expire and release the reserved seat.'
    $hrState=@(Invoke-Api -Method GET -Path '/api/advanced/waitlist' -BearerToken $hr.token)|Where-Object id -eq $hrWait.id
    Assert-That ($hrState.status -eq 'Offered') 'Expired offers promote the next waiting employee.'
    $null=Invoke-Api -Method POST -Path "/api/advanced/waitlist/$($proxyWait.id)/accept" -BearerToken $proxy.token -ExpectedStatus 409
    $calendar=@(Invoke-Api -Method GET -Path '/api/advanced/calendar' -BearerToken $learner.token)
    Assert-That (-not($calendar|Where-Object title -eq 'Workshop class A')) 'Cancelled enrollments are excluded from the learning calendar.'

    $skill=Invoke-Api -Method POST -Path '/api/advanced/skills' -Body @{name="Skill-$runToken";category='Technical';description='Levels 1 through 5'} -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path '/api/advanced/skill-requirements' -Body @{jobTitle=$employeeBody1.jobTitle;skillId=$skill.id;targetLevel=3}
    $null=Invoke-Api -Method POST -Path '/api/advanced/skill-courses' -Body @{skillId=$skill.id;courseId=$template.id;level=3}
    $assessment=Invoke-Api -Method POST -Path '/api/advanced/assessments' -Body @{skillId=$skill.id;level=2;evidence='Delivered an assessed project'} -BearerToken $learner.token -ExpectedStatus 201
    $assessmentRow=@(Invoke-Api -Method GET -Path '/api/advanced/assessments' -BearerToken $learner.token)|Where-Object id -eq $assessment.id
    $null=Invoke-Api -Method POST -Path "/api/advanced/assessments/$($assessment.id)/decision" -Body @{approve=$true;rowVersion=$assessmentRow.rowVersion} -BearerToken $learner.token -ExpectedStatus 403
    $null=Invoke-Api -Method POST -Path "/api/advanced/assessments/$($assessment.id)/decision" -Body @{approve=$true;rowVersion=$assessmentRow.rowVersion} -BearerToken $manager.token -ExpectedStatus 204
    $matrix=Invoke-Api -Method GET -Path '/api/advanced/skill-matrix' -BearerToken $learner.token
    Assert-That ($matrix.items[0].skills[0].currentLevel -eq 2 -and $matrix.items[0].skills[0].gap -eq 1) 'The competency matrix uses approved evidence and computes the job gap.'
    $goalBody=@{title='Reach level three';skillId=$skill.id;targetLevel=3;dueAt=[DateTimeOffset]::UtcNow.AddDays(30).ToString('o');status='Active';courseIds=@($template.id)}
    $goal=Invoke-Api -Method POST -Path '/api/advanced/goals' -Body $goalBody -BearerToken $learner.token -ExpectedStatus 201
    $goalRow=@(Invoke-Api -Method GET -Path '/api/advanced/goals' -BearerToken $learner.token)|Where-Object id -eq $goal.id
    $goalBody.status='Completed';$goalBody.rowVersion=$goalRow.rowVersion
    $null=Invoke-Api -Method PUT -Path "/api/advanced/goals/$($goal.id)" -Body $goalBody -BearerToken $learner.token -ExpectedStatus 409
    $assessment2=Invoke-Api -Method POST -Path '/api/advanced/assessments' -Body @{skillId=$skill.id;level=3;evidence='Passed a reviewed practical exercise'} -BearerToken $learner.token -ExpectedStatus 201
    $assessmentRow=@(Invoke-Api -Method GET -Path '/api/advanced/assessments' -BearerToken $manager.token)|Where-Object id -eq $assessment2.id
    $null=Invoke-Api -Method POST -Path "/api/advanced/assessments/$($assessment2.id)/decision" -Body @{approve=$true;rowVersion=$assessmentRow.rowVersion} -BearerToken $manager.token -ExpectedStatus 204
    $null=Invoke-Api -Method PUT -Path "/api/advanced/goals/$($goal.id)" -Body $goalBody -BearerToken $learner.token -ExpectedStatus 204
    $null=Invoke-Api -Method PUT -Path "/api/advanced/goals/$($goal.id)" -Body $goalBody -BearerToken $learner.token -ExpectedStatus 409

    $credentialType=Invoke-Api -Method POST -Path '/api/advanced/credential-types' -Body @{name="Certification-$runToken"} -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path '/api/advanced/credential-requirements' -Body @{jobTitle=$employeeBody1.jobTitle;credentialTypeId=$credentialType.id;renewalNoticeDays=60}
    $credentialValues=@{credentialTypeId=$credentialType.id;issuer='Test institute';credentialNumber="CERT-$runToken";issuedAt=[DateTimeOffset]::UtcNow.AddYears(-1).ToString('o');expiresAt=[DateTimeOffset]::UtcNow.AddDays(20).ToString('o')}
    $credential=Invoke-Advanced -Method POST -Path '/api/advanced/credentials' -Token $learner.token -Expected 201 -Content (New-EvidenceForm $credentialValues)
    $credentialRow=@(Invoke-Api -Method GET -Path '/api/advanced/credentials' -BearerToken $learner.token)|Where-Object id -eq $credential.id
    $null=Invoke-Api -Method POST -Path "/api/advanced/credentials/$($credential.id)/decision" -Body @{approve=$true;rowVersion=$credentialRow.rowVersion} -BearerToken $learner.token -ExpectedStatus 403
    $null=Invoke-Api -Method POST -Path "/api/advanced/credentials/$($credential.id)/decision" -Body @{approve=$true;rowVersion=$credentialRow.rowVersion} -BearerToken $manager.token -ExpectedStatus 204
    $compliance=@(Invoke-Api -Method GET -Path '/api/advanced/credential-compliance' -BearerToken $learner.token)
    Assert-That ($compliance[0].status -eq 'Expiring') 'Approved external credentials satisfy job requirements and report renewal deadlines.'
    $credentialValues.expiresAt=[DateTimeOffset]::UtcNow.AddYears(1).ToString('o');$credentialValues.replacesId=$credential.id
    $renewal=Invoke-Advanced -Method POST -Path '/api/advanced/credentials' -Token $learner.token -Expected 201 -Content (New-EvidenceForm $credentialValues)
    $renewalRow=@(Invoke-Api -Method GET -Path '/api/advanced/credentials' -BearerToken $manager.token)|Where-Object id -eq $renewal.id
    $null=Invoke-Api -Method POST -Path "/api/advanced/credentials/$($renewal.id)/decision" -Body @{approve=$true;rowVersion=$renewalRow.rowVersion} -BearerToken $manager.token -ExpectedStatus 204
    $compliance=@(Invoke-Api -Method GET -Path '/api/advanced/credential-compliance' -BearerToken $learner.token)
    Assert-That ($compliance[0].status -eq 'Valid') 'Renewing the same license number retains history and updates qualification status.'

    $client=Invoke-Api -Method POST -Path '/api/advanced/clients' -Body @{name='Provisioning and learning test';scopes=@('scim.read','scim.write','learning.read','learning.write');validDays=1} -ExpectedStatus 201
    foreach($metadataEndpoint in @('ResourceTypes','Schemas')) {
        $metadata=Invoke-Api -Method GET -Path ('/scim/v2/'+$metadataEndpoint) -BearerToken $client.token
        Assert-That (@($metadata.PSObject.Properties.Name) -ccontains 'Resources') 'SCIM metadata preserves the canonical Resources property casing.'
    }
    $readClient=Invoke-Api -Method POST -Path '/api/advanced/clients' -Body @{name='Read-only course feed';scopes=@('training.read');validDays=1} -ExpectedStatus 201
    $null=Invoke-Api -Method GET -Path '/scim/v2/Users' -BearerToken $readClient.token -ExpectedStatus 403
    $null=Invoke-Api -Method GET -Path '/api/enterprise/accounts' -BearerToken $client.token -ExpectedStatus 401
    $scimBody=@{schemas=@('urn:ietf:params:scim:schemas:core:2.0:User');userName="scim-$runToken";externalId="external-$runToken";displayName='SCIM colleague';active=$true;emails=@(@{value="scim-$runToken@example.test";primary=$true});'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User'=@{department='Enterprise Alpha';employeeNumber="SC-$runToken"}}
    $scim=Invoke-Api -Method POST -Path '/scim/v2/Users' -Body $scimBody -BearerToken $client.token -ExpectedStatus 201
    $null=Invoke-Api -Method PATCH -Path "/scim/v2/Users/$($scim.id)" -Body @{Operations=@(@{op='replace';path='active';value='not-a-boolean'})} -BearerToken $client.token -ExpectedStatus 400
    $scimFiltered=Invoke-Api -Method GET -Path ("/scim/v2/Users?filter="+[Uri]::EscapeDataString("userName eq `"scim-$runToken`"")) -BearerToken $client.token
    Assert-That ($scimFiltered.totalResults -eq 1 -and $scimFiltered.Resources[0].id -eq $scim.id) 'SCIM supports scoped user lookup, filtering, and provisioning.'
    $group=Invoke-Api -Method POST -Path '/scim/v2/Groups' -Body @{displayName='Beta directory group';externalId="group-$runToken";members=@(@{value=$scim.id})} -BearerToken $client.token -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path "/api/advanced/provisioning-groups/$($group.id)/department" -Body @{departmentId=$beta.id} -ExpectedStatus 204
    $scimAccount=@(Invoke-Api -Method GET -Path '/api/enterprise/accounts')|Where-Object id -eq ([int]$scim.id)
    $scimEmployee=Invoke-Api -Method GET -Path "/api/employees/$($scimAccount.employeeId)"
    Assert-That ($scimEmployee.department -eq 'Enterprise Beta') 'Explicit SCIM group mappings synchronize department membership.'
    $null=Invoke-Api -Method PATCH -Path "/scim/v2/Users/$($scim.id)" -Body @{schemas=@('urn:ietf:params:scim:api:messages:2.0:PatchOp');Operations=@(@{op='replace';path='active';value=$false})} -BearerToken $client.token
    $inactiveScim=Invoke-Api -Method GET -Path "/scim/v2/Users/$($scim.id)" -BearerToken $client.token
    Assert-That (-not $inactiveScim.active) 'SCIM offboarding disables the account while preserving its records.'
    $null=Invoke-Api -Method PATCH -Path "/scim/v2/Users/$($scim.id)" -Body @{Operations=@(@{op='replace';path='active';value=$true})} -BearerToken $client.token
    $null=Invoke-Api -Method GET -Path '/scim/v2/Users?filter=unsupported' -BearerToken $client.token -ExpectedStatus 400
    $null=Invoke-Api -Method POST -Path '/api/enrollments' -Body @{courseId=$template.id;employeeId=$scimEmployee.id} -ExpectedStatus 201
    $activityIri="https://training.example/activity/$runToken"
    $activity=Invoke-Api -Method POST -Path '/api/advanced/activities' -Body @{courseId=$template.id;title='External course activity';activityIri=$activityIri;launchUrl='https://training.example/lesson';clientId=$client.id;kind='External';requiredSeconds=60} -ExpectedStatus 201
    $statementId=[Guid]::NewGuid().ToString();$statement=@{id=$statementId;actor=@{objectType='Agent';mbox="mailto:scim-$runToken@example.test"};verb=@{id='http://adlnet.gov/expapi/verbs/completed'};object=@{objectType='Activity';id=$activityIri};result=@{completion=$true;duration='PT30M';score=@{scaled=0.9}}}
    $null=Invoke-Advanced -Method POST -Path '/api/xapi/statements' -Body $statement -Token $client.token -Xapi
    $null=Invoke-Advanced -Method PUT -Path "/api/xapi/statements?statementId=$statementId" -Body $statement -Token $client.token -Xapi -Expected 204
    $stored=Invoke-Advanced -Method GET -Path "/api/xapi/statements?statementId=$statementId" -Token $client.token -Xapi
    Assert-That ($stored.id -eq $statementId -and $stored.result.completion) 'xAPI records validated statements and returns the original activity evidence.'
    $statement.result.score.scaled=0.5
    $null=Invoke-Advanced -Method PUT -Path "/api/xapi/statements?statementId=$statementId" -Body $statement -Token $client.token -Xapi -Expected 409
    $records=@(Invoke-Api -Method GET -Path '/api/advanced/learning-records')|Where-Object id -eq $statementId
    Assert-That ($records.score -eq 90 -and $records.durationSeconds -eq 1800 -and $records.source -eq 'xAPI') 'Cross-platform records expose normalized score, duration and provenance.'
    $null=Invoke-Advanced -Method GET -Path '/api/xapi/statements' -Token $client.token -Expected 400
    $null=Invoke-Advanced -Method GET -Path '/api/xapi/statements' -Token $readClient.token -Xapi -Expected 403

    $csv="employeeNumber,name,email,department,jobTitle,isActive`r`nHR-$runToken,Automatic HR import,hr-$runToken@example.test,Enterprise Alpha,Specialist,true"
    [IO.File]::WriteAllText((Join-Path $stubDir 'employees.csv'),$csv,[Text.UTF8Encoding]::new($false))
    $schedule=Invoke-Api -Method POST -Path '/api/advanced/hr-schedules' -Body @{name='Automatic HR sync';intervalMinutes=15;allowDeactivation=$false;isActive=$true} -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path "/api/advanced/hr-schedules/$($schedule.id)/run"
    $syncState=@(Invoke-Api -Method GET -Path '/api/advanced/hr-schedules')|Where-Object id -eq $schedule.id
    Assert-That ($syncState.lastStatus -eq 'Success') 'Scheduled HR ingestion uses preview validation and commits valid personnel updates.'
    $csv=$csv.Replace(',true',',false');[IO.File]::WriteAllText((Join-Path $stubDir 'employees.csv'),$csv,[Text.UTF8Encoding]::new($false))
    $null=Invoke-Api -Method POST -Path "/api/advanced/hr-schedules/$($schedule.id)/run"
    $syncState=@(Invoke-Api -Method GET -Path '/api/advanced/hr-schedules')|Where-Object id -eq $schedule.id
    Assert-That ($syncState.lastStatus -eq 'Failed') 'Automatic offboarding requires the schedule to explicitly permit deactivation.'

    $subscription=Invoke-Api -Method POST -Path '/api/advanced/webhooks' -Body @{name='Loopback event test';endpoint="http://127.0.0.1:$stubPort/webhook";events=@('course.published')} -ExpectedStatus 201
    $null=Invoke-Api -Method POST -Path '/api/advanced/webhooks' -Body @{name='Unsafe private endpoint';endpoint='https://10.0.0.1/hook';events=@('course.published')} -ExpectedStatus 400
    $null=Invoke-Api -Method POST -Path '/api/courses' -Body (New-CourseBody -Capacity 3) -ExpectedStatus 201
    $delivery=$null
    for($i=0;$i -lt 5 -and $null -eq $delivery;$i++){$null=Invoke-Api -Method POST -Path '/api/advanced/webhooks/process';$delivery=@(Invoke-Api -Method GET -Path '/api/advanced/webhook-deliveries')|Where-Object subscriptionId -eq $subscription.id|Select-Object -First 1}
    Assert-That ($delivery.status -eq 'Retry' -and $delivery.lastHttpStatus -eq 503) 'Failed webhooks enter the retry queue without losing the event.'
    $null=Invoke-Api -Method POST -Path "/api/advanced/webhook-deliveries/$($delivery.id)/retry" -ExpectedStatus 204
    $null=Invoke-Api -Method POST -Path '/api/advanced/webhooks/process'
    $delivery=@(Invoke-Api -Method GET -Path '/api/advanced/webhook-deliveries')|Where-Object id -eq $delivery.id
    Assert-That ($delivery.status -eq 'Delivered') 'Manual redrive successfully delivers the same event.'
    $received=@(Get-Content (Join-Path $stubDir 'requests.jsonl')|ForEach-Object{ConvertFrom-Json -InputObject $_})
    $first=$received[0];$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($subscription.secret));try{$signature=[BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($first.headers.'x-training-timestamp'+'.'+$first.body))).Replace('-','').ToLowerInvariant()}finally{$hmac.Dispose()}
    Assert-That ($first.headers.'x-training-signature' -eq 'v1='+$signature) 'Webhook payloads carry a verifiable timestamp-bound HMAC signature.'
    Assert-That ($received[0].headers.'x-training-event-id' -eq $received[1].headers.'x-training-event-id') 'Retries preserve the event identity for receiver-side deduplication.'
    $clientRows=Invoke-Api -Method GET -Path '/api/advanced/clients'
    Assert-That (($clientRows|ConvertTo-Json -Depth 10) -notmatch 'TokenHash|tm_') 'Service credential lists never reveal the key or its hash.'
    $null=Invoke-Api -Method POST -Path "/api/advanced/clients/$($client.id)/revoke" -ExpectedStatus 204
    $null=Invoke-Api -Method GET -Path '/scim/v2/Users' -BearerToken $client.token -ExpectedStatus 401
} finally {if($stubProcess){if(-not $stubProcess.HasExited){Stop-Process -InputObject $stubProcess -Force;$stubProcess.WaitForExit()};$stubProcess.Dispose()}}
