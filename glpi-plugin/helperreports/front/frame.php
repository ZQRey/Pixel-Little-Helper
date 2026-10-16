<?php
Session::checkRight('statistic', READ);
header('Cache-Control: no-store');
header("Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'self'; base-uri 'self'; form-action 'self'");
$assets = htmlspecialchars(Plugin::getWebDir('helperreports') . '/assets/', ENT_QUOTES, 'UTF-8');
?>
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Статистика и оценки</title>
<link rel="stylesheet" href="<?= $assets ?>styles.css?v=0.2.0"><link rel="stylesheet" href="<?= $assets ?>glpi-reports.css?v=0.5.0"><link rel="stylesheet" href="<?= $assets ?>portal.css?v=0.2.0">
<script defer src="<?= $assets ?>glpi-reports.js?v=0.5.1"></script><script defer src="<?= $assets ?>portal.js?v=0.2.0"></script></head>
<body><main><div class="portal-heading"><div><h1>Статистика и оценки</h1><p>Доступ по текущему сеансу и выбранным организациям GLPI.</p></div><button id="refresh-button">↻ Обновить</button></div><div id="stats" hidden></div><section id="content">Загрузка отчёта…</section></main>
<dialog id="modal"><div class="modal-heading"><h2 id="modal-title"></h2><button id="modal-close" aria-label="Закрыть">×</button></div><div id="modal-content"></div></dialog><div id="toast" role="status" hidden></div></body></html>

