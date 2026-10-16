<?php
Session::checkRight('statistic', READ);
header('Cache-Control: no-store');
header('X-Content-Type-Options: nosniff');
try {
    if ($_SERVER['REQUEST_METHOD'] !== 'GET') { http_response_code(405); exit; }
    $path = $_GET['path'] ?? '';
    $parts = parse_url($path);
    $operations = ['/reports/glpi' => 'report', '/reports/glpi/export/xlsx' => 'xlsx', '/reports/glpi/export/pdf' => 'pdf'];
    $operation = $operations[$parts['path'] ?? ''] ?? null;
    if (!$operation || isset($parts['host']) || strlen($path) > 6500) throw new RuntimeException('Недопустимая операция отчёта');
    $dir = GLPI_PLUGIN_DOC_DIR . '/helperreports';
    $key = trim(file_get_contents($dir . '/bridge.key'));
    $target = parse_url(trim(file_get_contents($dir . '/panel-url.txt')));
    if (($target['scheme'] ?? '') !== 'https' || empty($target['host']) || strlen($key) < 32) throw new RuntimeException('Мост отчёта не настроен');
    $body = json_encode(['operation' => $operation, 'query' => $parts['query'] ?? '', 'userId' => (int)Session::getLoginUserID(), 'username' => (string)($_SESSION['glpiname'] ?? ''), 'entities' => array_values(array_map('intval', Session::getActiveEntities()))], JSON_UNESCAPED_UNICODE | JSON_THROW_ON_ERROR);
    $time = (string)time(); $nonce = bin2hex(random_bytes(16));
    $signature = hash_hmac('sha256', "GLPI-REPORTS\n" . $time . "\n" . $nonce . "\n" . $body, $key);
    $port = isset($target['port']) ? ':' . (int)$target['port'] : '';
    $curl = curl_init('https://' . $target['host'] . $port . '/api/glpi-report-portal');
    curl_setopt_array($curl, [CURLOPT_POST => true, CURLOPT_POSTFIELDS => $body, CURLOPT_RETURNTRANSFER => true, CURLOPT_FOLLOWLOCATION => false, CURLOPT_CONNECTTIMEOUT => 10, CURLOPT_TIMEOUT => 90, CURLOPT_HTTPHEADER => ['Content-Type: application/json', 'X-Helper-Time: ' . $time, 'X-Helper-Nonce: ' . $nonce, 'X-Helper-Signature: ' . $signature]]);
    if (is_file($dir . '/helper-ca.pem')) curl_setopt($curl, CURLOPT_CAINFO, $dir . '/helper-ca.pem');
    session_write_close();
    $result = curl_exec($curl); $status = curl_getinfo($curl, CURLINFO_RESPONSE_CODE);
    if ($result === false || $status < 100) { curl_close($curl); throw new RuntimeException('Сервер отчётов временно недоступен'); }
    curl_close($curl); http_response_code($status);
    $success = $status >= 200 && $status < 300;
    header('Content-Type: ' . ($success && $operation === 'pdf' ? 'application/pdf' : ($success && $operation === 'xlsx' ? 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' : 'application/json')));
    if ($success && $operation !== 'report') header('Content-Disposition: attachment; filename="GLPI-report.' . $operation . '"');
    echo $result;
} catch (Throwable $e) {
    http_response_code(502); header('Content-Type: application/json');
    echo json_encode(['error' => 'Не удалось получить отчёт. Проверьте подключение GLPI к серверу помощника.']);
}
