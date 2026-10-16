<?php
Session::checkRight('statistic', READ);
Html::header('Статистика и оценки', '', 'helpdesk', 'PluginHelperreportsMenu');
$src = htmlspecialchars(Plugin::getWebDir('helperreports') . '/front/frame.php', ENT_QUOTES, 'UTF-8');
echo '<iframe src="' . $src . '" title="Статистика GLPI и оценки" width="100%" height="1600" frameborder="0"></iframe>';
Html::footer();
