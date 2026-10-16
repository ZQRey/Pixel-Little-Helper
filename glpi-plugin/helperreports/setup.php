<?php
function plugin_version_helperreports(): array {
    return ['name' => 'Helper Reports', 'version' => '0.5.1', 'author' => 'Internal IT', 'license' => 'GPLv3+',
        'requirements' => ['glpi' => ['min' => '11.0.8', 'max' => '11.1']]];
}
function plugin_init_helperreports(): void {
    global $PLUGIN_HOOKS;
    $PLUGIN_HOOKS['csrf_compliant']['helperreports'] = true;
    \Glpi\Http\SessionManager::registerPluginStatelessPath('helperreports', '#^/Bridge$#');
    $PLUGIN_HOOKS['menu_toadd']['helperreports'] = ['helpdesk' => 'PluginHelperreportsMenu'];
    require_once __DIR__ . '/inc/surveys.php';
    $PLUGIN_HOOKS['pre_item_add']['helperreports'] = ['Ticket' => 'plugin_helperreports_before_ticket_add'];
    $PLUGIN_HOOKS['add_javascript']['helperreports'] = 'js/surveys.js';
}
function plugin_helperreports_check_prerequisites(): bool { return true; }
function plugin_helperreports_check_config(): bool { return true; }
