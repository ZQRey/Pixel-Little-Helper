<?php
/** The GLPI session is the only source of requester identity. No panel token is issued. */
function plugin_helperreports_survey_request(string $operation, array $input = []): array {
    if (!Session::getLoginUserID()) throw new RuntimeException('Требуется вход в GLPI');
    $dir = GLPI_PLUGIN_DOC_DIR . '/helperreports';
    $key = trim(file_get_contents($dir . '/bridge.key'));
    $target = parse_url(trim(file_get_contents($dir . '/panel-url.txt')));
    if (strlen($key) < 32 || ($target['scheme'] ?? '') !== 'https' || empty($target['host'])) throw new RuntimeException('Интеграция оценок не настроена');
    $body = json_encode(['operation'=>$operation,'query'=>json_encode($input,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR),
        'userId'=>(int)Session::getLoginUserID(),'username'=>(string)($_SESSION['glpiname']??''),
        'entities'=>array_values(array_map('intval',Session::getActiveEntities()))],JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR);
    $time=(string)time(); $nonce=bin2hex(random_bytes(16));
    $signature=hash_hmac('sha256',"GLPI-REPORTS\n".$time."\n".$nonce."\n".$body,$key);
    $port=isset($target['port'])?':'.(int)$target['port']:'';
    $curl=curl_init('https://'.$target['host'].$port.'/api/glpi-report-portal');
    curl_setopt_array($curl,[CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>$body,CURLOPT_RETURNTRANSFER=>true,CURLOPT_FOLLOWLOCATION=>false,
        CURLOPT_CONNECTTIMEOUT=>5,CURLOPT_TIMEOUT=>45,CURLOPT_HTTPHEADER=>['Content-Type: application/json','X-Helper-Time: '.$time,'X-Helper-Nonce: '.$nonce,'X-Helper-Signature: '.$signature]]);
    if(is_file($dir.'/helper-ca.pem'))curl_setopt($curl,CURLOPT_CAINFO,$dir.'/helper-ca.pem');
    $raw=curl_exec($curl);$status=curl_getinfo($curl,CURLINFO_RESPONSE_CODE);curl_close($curl);
    $result=is_string($raw)?json_decode($raw,true):null;
    if(!is_array($result)||$status<200||$status>=300)throw new RuntimeException(is_array($result)?($result['error']??'Сервис оценок недоступен'):'Сервис оценок недоступен');
    return $result;
}
function plugin_helperreports_before_ticket_add($ticket): void {
    // Service/API creation and technician workflows keep their existing behavior.
    if (!$ticket instanceof Ticket || !is_array($ticket->input) || ($_SESSION['glpiactiveprofile']['interface']??'')!=='helpdesk'
        || PHP_SAPI==='cli' || defined('GLPI_API') && GLPI_API || ($_SERVER['REQUEST_METHOD']??'')!=='POST') return;
    $urgent=($ticket->input['_helper_survey_urgent']??'')==='1';
    try {
        $gate=plugin_helperreports_survey_request('survey-gate');
        if(empty($gate['required']))return;
        if($urgent){ plugin_helperreports_survey_request('survey-urgent');Toolbox::logInFile('helperreports','Urgent support bypass by GLPI user '.(int)Session::getLoginUserID()."\n");return; }
        $ticket->input=false;
        $url=htmlspecialchars(Plugin::getWebDir('helperreports').'/front/surveys.php',ENT_QUOTES,'UTF-8');
        Session::addMessageAfterRedirect('Оцените выполненную заявку перед новым обычным обращением: <a href="'.$url.'">Ожидают оценки</a>. Для срочной проблемы отметьте «Срочное обращение».',false,ERROR);
    } catch (Throwable $e) {
        // An integration outage must not prevent contacting support.
        Toolbox::logInFile('helperreports',"Survey gate unavailable; ticket creation allowed\n");
    }
}
