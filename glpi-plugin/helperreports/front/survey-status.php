<?php
Session::checkLoginUser();
header('Content-Type: application/json');header('Cache-Control: no-store');
require_once dirname(__DIR__).'/inc/surveys.php';
try { echo json_encode(plugin_helperreports_survey_request('survey-list'),JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR); }
catch(Throwable $e){ http_response_code(503);echo json_encode(['error'=>'Не удалось получить список оценок']); }
