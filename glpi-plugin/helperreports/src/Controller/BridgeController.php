<?php
namespace GlpiPlugin\Helperreports\Controller;

use Glpi\Controller\AbstractController;
use Glpi\Http\Firewall;
use Glpi\Security\Attribute\SecurityStrategy;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpFoundation\Request;
use Symfony\Component\Routing\Attribute\Route;

final class BridgeController extends AbstractController
{
    #[Route('/Bridge', name: 'helperreports_bridge', methods: ['POST'])]
    #[SecurityStrategy(Firewall::STRATEGY_NO_CHECK)]
    public function __invoke(Request $request): JsonResponse
    {
        global $DB;
        $dir = GLPI_PLUGIN_DOC_DIR . '/helperreports';
        $key = is_file($dir . '/bridge.key') ? trim(file_get_contents($dir . '/bridge.key')) : '';
        $raw = $request->getContent();
        $timestamp = $request->headers->get('X-Helper-Time', '');
        $nonce = $request->headers->get('X-Helper-Nonce', '');
        $signature = $request->headers->get('X-Helper-Signature', '');
        if (strlen($key) < 32 || strlen($raw) > 12000 || !ctype_digit($timestamp) || abs(time() - (int)$timestamp) > 120
            || !preg_match('/^[a-f0-9]{32}$/D', $nonce)
            || !hash_equals(hash_hmac('sha256', $timestamp . "\n" . $nonce . "\n" . $raw, $key), $signature)) {
            return new JsonResponse(['error' => 'Неверная подпись интеграции'], 401);
        }
        if (!is_dir($dir . '/requests') && !mkdir($dir . '/requests', 0700, true) && !is_dir($dir . '/requests')) {
            return new JsonResponse(['error' => 'Хранилище запросов недоступно'], 503);
        }
        // Timestamp bounds nonce retention. Atomic create prevents concurrent replay.
        foreach (glob($dir . '/requests/nonce-*') ?: [] as $file) if (filemtime($file) < time() - 300) @unlink($file);
        $seen = @fopen($dir . '/requests/nonce-' . $nonce, 'x');
        if (!$seen) return new JsonResponse(['error' => 'Повтор подписанного запроса'], 409);
        fclose($seen);
        try { $body = json_decode($raw, true, 32, JSON_THROW_ON_ERROR); }
        catch (\Throwable $e) { return new JsonResponse(['error' => 'Некорректный JSON'], 400); }
        if (($body['operation'] ?? '') === 'probe') return new JsonResponse(['ready' => true, 'storage' => 'TicketSatisfaction', 'version' => '0.1.0', 'timezone' => date_default_timezone_get()]);
        $id = $body['ticketId'] ?? 0;
        $uid = $body['requesterId'] ?? 0;
        $score = $body['score'] ?? 0;
        $login = $body['username'] ?? '';
        $comment = $body['comment'] ?? '';
        $requestId = $body['requestId'] ?? '';
        if (($body['operation'] ?? '') !== 'answer' || !is_int($id) || $id < 1 || !is_int($uid) || $uid < 1 || !is_int($score) || $score < 1 || $score > 5
            || !is_string($login) || strlen($login) > 255 || !is_string($comment) || mb_strlen($comment) > 2000
            || !is_string($requestId) || !preg_match('/^[a-f0-9-]{36}$/Di', $requestId)) return new JsonResponse(['error' => 'Некорректная оценка'], 400);
        $lock = fopen($dir . '/requests/ticket-' . $id . '.lock', 'c');
        if (!$lock || !flock($lock, LOCK_EX)) return new JsonResponse(['error' => 'Повторите попытку'], 503);
        $previousSession = $_SESSION ?? [];
        $transaction = false;
        try {
            $DB->beginTransaction(); $transaction = true;
            $DB->doQuery("SELECT id FROM glpi_tickets WHERE id=" . $id . " FOR UPDATE");
            $DB->doQuery("SELECT id FROM glpi_ticketsatisfactions WHERE tickets_id=" . $id . " FOR UPDATE");
            $ticket = new \Ticket(); $user = new \User();
            $closedUtc = null;
            if ($ticket->getFromDB($id) && !empty($ticket->fields['closedate'])) {
                $dbZone = new \DateTimeZone($DB->use_timezones ? date_default_timezone_get() : 'UTC');
                $closedUtc = (new \DateTimeImmutable($ticket->fields['closedate'], $dbZone))->setTimezone(new \DateTimeZone('UTC'))->format('Y-m-d H:i:s');
            }
            if (!$ticket->getFromDB($id) || (int)$ticket->fields['is_deleted'] !== 0 || (int)$ticket->fields['status'] !== 6
                || $closedUtc !== ($body['closedAt'] ?? '') || strtotime($closedUtc . ' UTC') < time() - 14 * DAY_TIMESTAMP) {
                return new JsonResponse(['error' => 'Заявка переоткрыта, удалена или срок оценки истёк'], 409);
            }
            if (!$user->getFromDB($uid) || (int)$user->fields['is_active'] !== 1 || (int)$user->fields['is_deleted'] !== 0 || strcasecmp($user->fields['name'], $login) !== 0) return new JsonResponse(['error' => 'Заявитель не подтверждён'], 403);
            $requesters = iterator_to_array($DB->request(['FROM' => 'glpi_tickets_users', 'WHERE' => ['tickets_id' => $id, 'type' => 1], 'ORDERBY' => 'id']));
            if (!$requesters || (int)reset($requesters)['users_id'] !== $uid) return new JsonResponse(['error' => 'Оценку выставляет основной заявитель'], 403);
            foreach ($DB->request(['FROM' => 'glpi_tickets_users', 'WHERE' => ['tickets_id' => $id, 'type' => 2]]) as $actor) if ((int)$actor['users_id'] === $uid) return new JsonResponse(['error' => 'Исполнитель не оценивает свою работу'], 403);
            foreach ($DB->request(['FROM' => 'glpi_itilsolutions', 'WHERE' => ['itemtype' => 'Ticket', 'items_id' => $id, 'users_id' => $uid]]) as $_) return new JsonResponse(['error' => 'Автор решения не оценивает свою работу'], 403);
            $hash = hash('sha256', json_encode([$uid, $score, $comment]));
            $receiptFile = $dir . '/requests/answer-' . $id . '.json';
            $receipt = is_file($receiptFile) ? json_decode(file_get_contents($receiptFile), true) : null;
            if ($receipt && $receipt['requestId'] === $requestId) {
                if (!hash_equals($receipt['hash'], $hash)) return new JsonResponse(['error' => 'Ключ повтора использован для другого ответа'], 409);
                return new JsonResponse(['saved' => true, 'duplicate' => true]);
            }
            $entity = (int)$ticket->fields['entities_id'];
            if ((int)\Entity::getUsedConfig('inquest_config', $entity, 'inquest_max_rate') !== 5) return new JsonResponse(['error' => 'Шкала опроса GLPI должна быть 1–5'], 409);
            // The helper authenticated this requester. Use an isolated, non-cookie session only
            // after checking the immutable GLPI user ID and the actual requester relationship.
            $_SESSION = ['glpiID' => $uid, 'glpiname' => $login, 'glpi_currenttime' => date('Y-m-d H:i:s'), 'glpiactive_entity' => $entity,
                'glpiactiveentities' => [$entity], 'glpigroups' => [], 'glpiactiveprofile' => ['interface' => 'helpdesk', 'ticket' => \Ticket::SURVEY]];
            $survey = new \TicketSatisfaction();
            $exists = $survey->getFromDBByCrit(['tickets_id' => $id]);
            if ($exists && $survey->fields['date_answered'] !== null) return new JsonResponse(['error' => 'Оценка уже сохранена. Изменение доступно в GLPI в течение 12 часов'], 409);
            if (!$exists) {
                $sid = $survey->add(['tickets_id' => $id, 'type' => 1, 'date_begin' => date('Y-m-d H:i:s'), '_disablenotif' => true]);
                if (!$sid || !$survey->getFromDB($sid)) throw new \RuntimeException('Survey creation failed');
            }
            if (!$survey->canUpdateItem() || !$survey->update(['tickets_id' => $id, 'satisfaction' => $score, 'comment' => $comment, '_disablenotif' => true])) throw new \RuntimeException('Survey update failed');
            if (!$survey->getFromDB($survey->getID()) || (int)$survey->fields['satisfaction'] !== $score || $survey->fields['date_answered'] === null) throw new \RuntimeException('Survey not confirmed');
            $DB->commit(); $transaction = false;
            file_put_contents($receiptFile, json_encode(['requestId' => $requestId, 'hash' => $hash, 'at' => date('c')]));
            return new JsonResponse(['saved' => true, 'surveyId' => (int)$survey->fields['id']]);
        } catch (\Throwable $e) {
            if ($transaction) { $DB->rollBack(); $transaction = false; }
            error_log("HelperReports: " . get_class($e) . " at line " . $e->getLine());
            return new JsonResponse(['error' => 'GLPI не сохранил оценку. Проверьте настройки и обязательный комментарий'], 502);
        } finally {
            if ($transaction) $DB->rollBack();
            $_SESSION = $previousSession;
            flock($lock, LOCK_UN); fclose($lock);
        }
    }
}
