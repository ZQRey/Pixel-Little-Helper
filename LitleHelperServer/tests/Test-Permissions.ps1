param([string]$Dotnet)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$folder=Join-Path $root ('artifacts/permissions-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $folder|Out-Null
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$url="http://127.0.0.1:$port"
$start=[Diagnostics.ProcessStartInfo]::new($Dotnet);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$root
$start.ArgumentList.Add((Join-Path $root 'bin/Release/net8.0/LitleHelperServer.dll'));$start.ArgumentList.Add('--urls');$start.ArgumentList.Add($url)
$start.Environment['Database__Provider']='Sqlite';$start.Environment['ConnectionStrings__Database']="Data Source=$folder/helper.db"
$start.Environment['Integrations__SettingsFile']="$folder/integrations.json";$start.Environment['Jwt__SigningKey']=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
function Call($path,$method='GET',$body=$null,$token=$null){
 $args=@{Uri="$url/api$path";Method=$method;SkipHttpErrorCheck=$true}
 if($token){$args.Headers=@{Authorization="Bearer $token"}}
 if($null-ne $body){$args.ContentType='application/json';$args.Body=$body|ConvertTo-Json -Depth 6}
 $r=Invoke-WebRequest @args;[pscustomobject]@{Status=[int]$r.StatusCode;Data=($r.Content|ConvertFrom-Json)}
}
function Check($ok,$message){if(!$ok){throw $message};"PASS $message"}
$server=[Diagnostics.Process]::Start($start)
try {
 for($i=0;$i-lt 50;$i++){try{Invoke-RestMethod "$url/health"|Out-Null;break}catch{Start-Sleep -Milliseconds 200}}
 $login=Call '/auth/login' 'POST' @{username='admin';password='admin123'};$admin=$login.Data.token
 $changed=Call '/auth/change-password' 'POST' @{currentPassword='admin123';newPassword='Permission-test-123'} $admin;$admin=$changed.Data.token
 $catalog=Call '/permissions' 'GET' $null $admin;Check ($catalog.Status-eq 200 -and $catalog.Data.catalog.'users.manage') 'permission catalog available to super administrator'
 $request=@{username='delegate';fullName='Delegated User';role='User';isActive=$true;password='Permission-test-123';permissions=@{'computers.view'=$true;'buttons.manage'=$true}}
 $created=Call '/users' 'POST' $request $admin;Check ($created.Status-eq 200) 'create user with individual grants'
 $login=Call '/auth/login' 'POST' @{username='delegate';password='Permission-test-123'};$delegate=$login.Data.token
 $changed=Call '/auth/change-password' 'POST' @{currentPassword='Permission-test-123';newPassword='Permission-test-456'} $delegate;$delegate=$changed.Data.token
 Check ((Call '/computers' 'GET' $null $delegate).Status-eq 200) 'User granted computer viewing'
 Check ((Call '/buttons' 'GET' $null $delegate).Status-eq 200) 'User granted menu access'
 Check ((Call '/users' 'GET' $null $delegate).Status-eq 403) 'individual grants do not expose user management'
 Check ((Call '/settings/ad' 'GET' $null $delegate).Status-eq 403) 'individual grants do not expose AD settings'
 Check ((Call '/commands' 'POST' @{machines=@('TEST');type='cmd';payload='echo test'} $delegate).Status-eq 403) 'User cannot execute commands without permission'
 $request.password='';$request.permissions=@{'users.manage'=$true;'commands.execute'=$true};$request.role='Admin'
 $updated=Call "/users/$($created.Data.id)" 'PUT' $request $admin;Check ($updated.Status-eq 200) 'update role and overrides'
 Check ((Call '/auth/me' 'GET' $null $delegate).Status-eq 401) 'permission changes revoke old JWT'
 $login=Call '/auth/login' 'POST' @{username='delegate';password='Permission-test-456'};$delegate=$login.Data.token
 Check ((Call '/users' 'GET' $null $delegate).Status-eq 200) 'delegated user management works'
 Check ((Call '/commands' 'POST' @{machines=@('TEST');type='cmd';payload='echo test'} $delegate).Status-eq 403) 'command permission does not grant terminal'
 $request.permissions=@{'computers.view'=$false};$updated=Call "/users/$($created.Data.id)" 'PUT' $request $admin
 $login=Call '/auth/login' 'POST' @{username='delegate';password='Permission-test-456'};$delegate=$login.Data.token
 Check ((Call '/computers' 'GET' $null $delegate).Status-eq 403) 'explicit deny overrides Admin role'
 $request.permissions=@{'invalid.permission'=$true};Check ((Call "/users/$($created.Data.id)" 'PUT' $request $admin).Status-eq 400) 'unknown permissions rejected'
 $users=(Call '/users' 'GET' $null $admin).Data;$super=$users|Where-Object username -eq 'admin'
 Check ((Call "/users/$($super.id)" 'PUT' @{username='admin';fullName='Admin';role='User';isActive=$true;password=''} $admin).Status-eq 400) 'last super administrator protected'
 $server.Kill();$server.WaitForExit();$server.Dispose();$server=[Diagnostics.Process]::Start($start)
 for($i=0;$i-lt 50;$i++){try{Invoke-RestMethod "$url/health"|Out-Null;break}catch{Start-Sleep -Milliseconds 200}}
 $login=Call '/auth/login' 'POST' @{username='delegate';password='Permission-test-456'}
 Check ((Call '/computers' 'GET' $null $login.Data.token).Status-eq 403) 'overrides survive restart and repeated schema initialization'
} finally {if(!$server.HasExited){$server.Kill();$server.WaitForExit()};$server.Dispose()}
