<?php
Session::checkLoginUser();
header('Cache-Control: no-store');
require_once dirname(__DIR__).'/inc/surveys.php';
$error='';$message='';
if(($_SERVER['REQUEST_METHOD']??'')==='POST'){
    // GLPI's front controller validates its CSRF token before this script is executed.
    try {
        $id=(string)($_POST['id']??'');
        if(($_POST['action']??'')==='answer'){
            $result=plugin_helperreports_survey_request('survey-answer',['id'=>$id,'score'=>(int)($_POST['score']??0),'comment'=>(string)($_POST['comment']??''),'requestId'=>(string)($_POST['requestId']??'')]);
            if(empty($result['saved'])&&empty($result['queued']))throw new RuntimeException($result['error']??'GLPI не подтвердил запись');
            $message=!empty($result['saved'])?'Оценка сохранена в GLPI. Спасибо.':'Оценка принята помощником и ожидает записи в GLPI.';
        } else plugin_helperreports_survey_request('survey-snooze',['id'=>$id,'minutes'=>(int)($_POST['minutes']??30)]);
    }catch(Throwable $e){$error=$e->getMessage();}
}
if(($_SESSION['glpiactiveprofile']['interface']??'')==='helpdesk')Html::helpHeader('Оценить выполненные заявки');
else Html::header('Оценить выполненные заявки',$_SERVER['PHP_SELF']);
$esc=fn($v)=>htmlspecialchars((string)$v,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
echo '<main class="container"><h1>Ожидают оценки</h1><p>Оценка от 1 до 5. Комментарий необязателен. Срок — 14 дней после закрытия заявки.</p>';
if($error!=='')echo '<p class="alert alert-danger">'.$esc($error).'</p>';
if($message!=='')echo '<p class="alert alert-success">'.$esc($message).'</p>';
try{
    $rows=plugin_helperreports_survey_request('survey-list');
    if(!$rows)echo '<p>Нет заявок, ожидающих вашей оценки.</p>';
    foreach($rows as $row){
        echo '<section class="card p-3 mb-3"><h2>#'.(int)$row['ticketId'].' · '.$esc($row['title']).'</h2><p>'.$esc($row['specialist']).'</p>';
        echo '<p>'.(!empty($row['mandatory'])?'Обязательная оценка. ':'').'Оценить до '.$esc($row['expiresAt']).'</p>';
        if($row['state']==='Writing'){echo '<p>Оценка принята помощником и ожидает записи в GLPI.</p></section>';continue;}
        if(!empty($row['error']))echo '<p class="alert alert-warning">'.$esc($row['error']).'</p>';
        if($row['state']==='Pending')plugin_helperreports_survey_request('survey-claim',['id'=>$row['id']]);
        $bytes=random_bytes(16);$bytes[6]=chr((ord($bytes[6])&15)|64);$bytes[8]=chr((ord($bytes[8])&63)|128);$hex=bin2hex($bytes);
        $requestId=substr($hex,0,8).'-'.substr($hex,8,4).'-'.substr($hex,12,4).'-'.substr($hex,16,4).'-'.substr($hex,20);
        echo '<form method="post"><input type="hidden" name="_glpi_csrf_token" value="'.$esc(Session::getNewCSRFToken()).'"><input type="hidden" name="id" value="'.$esc($row['id']).'"><input type="hidden" name="requestId" value="'.$requestId.'"><fieldset><legend>Ваша оценка</legend>';
        for($n=1;$n<=5;$n++)echo '<label class="me-3"><input type="radio" name="score" value="'.$n.'" required> '.$n.' ★</label>';
        echo '</fieldset><label class="d-block mt-2">Комментарий (необязательно)<textarea class="form-control" name="comment" maxlength="2000"></textarea></label><button class="btn btn-primary mt-2" name="action" value="answer">Отправить оценку</button>';
        if(empty($row['mandatory'])||($row['deferralsLeft']??0)>0)foreach([30=>'На 30 минут',120=>'На 2 часа'] as $minutes=>$label)echo '<button class="btn btn-secondary mt-2 ms-2" name="minutes" value="'.$minutes.'" formnovalidate>'.$label.'</button>';
        echo '</form></section>';
    }
}catch(Throwable $e){echo '<p class="alert alert-warning">'.$esc($e->getMessage()).'</p>';}
echo '</main>';
if(($_SESSION['glpiactiveprofile']['interface']??'')==='helpdesk')Html::helpFooter();else Html::footer();
