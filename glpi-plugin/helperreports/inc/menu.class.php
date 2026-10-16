<?php
class PluginHelperreportsMenu extends CommonGLPI {
    public static function getMenuName() { return 'Статистика и оценки'; }
    public static function getMenuContent() {
        if (!Session::haveRight('statistic', READ)) return ['title'=>'Ожидают оценки','page'=>'/plugins/helperreports/front/surveys.php','icon'=>'ti ti-star'];
        return ['title' => self::getMenuName(), 'page' => '/plugins/helperreports/front/reports.php', 'icon' => 'ti ti-chart-bar'];
    }
}
