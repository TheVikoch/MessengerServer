# Инструкция по файловому стримингу (stream-чаты)

Документ описывает все изменения и то, как пользоваться новой логикой.

**Что реализовано**
1. Stream-чат как отдельный тип беседы (`Conversation.Type = "stream"`).
2. Инвайты в stream-чат через отдельную таблицу.
3. Потоковая передача файлов через SignalR без сохранения файла на сервере.
4. Контроль целостности и выборочная повторная отправка чанков.
5. Технические сообщения с `Kind` и `MetadataJson`.

**Ключевые сущности и файлы**
1. `Models/Message.cs` — поля `Kind` и `MetadataJson`.
2. `Models/StreamChatInvite.cs` — инвайты.
3. `Models/DTOs/StreamTransferDtos.cs` — DTO для стриминга.
4. `Services/stream/StreamTransferService.cs` — in-memory сессии.
5. `Hubs/MessengerHub.cs` — методы стрима.
6. `Controllers/StreamInvitesController.cs` — API инвайтов.
7. `appsettings.json` — конфиг `StreamTransfer`.

## Инвайты

**Срок жизни**: 1 час.  
**Ограничение**: один активный инвайт на персональный чат.

**API**
1. `POST /api/stream-invites`  
Тело:
```json
{
  "personalChatId": "GUID",
  "streamChatName": "Название (опционально)"
}
```
Ответ:
```json
{
  "inviteId": "GUID",
  "personalChatId": "GUID",
  "creatorId": "GUID",
  "targetUserId": "GUID",
  "token": "string",
  "streamChatName": "string",
  "expiresAt": "2026-03-24T12:34:56Z"
}
```

2. `POST /api/stream-invites/accept`  
Тело:
```json
{
  "token": "string"
}
```
Результат: создается stream-чат, пишется системное сообщение в основной чат.

3. `POST /api/stream-invites/revoke`  
Тело:
```json
{
  "inviteId": "GUID"
}
```
Результат: инвайт отозван, пишется системное сообщение в основной чат.

**Системные сообщения**
1. `Kind = "stream_invite_accepted"`
2. `Kind = "stream_invite_revoked"`

## Стрим-файлы (SignalR)

**Ограничение**: в одном stream-чате одновременно передается только один файл.  
**Размер чанка**: 512 KB.  
**Окно**: 64 чанка.  
**TTL сессии**: 60 минут после последней активности.

**События**
1. `stream_transfer_offer` — предложение принять файл.
2. `stream_transfer_accepted`
3. `stream_transfer_rejected`
4. `stream_transfer_chunk`
5. `stream_transfer_ack`
6. `stream_transfer_nack`
7. `stream_transfer_resume`
8. `stream_transfer_complete`
9. `stream_transfer_canceled`

**Порядок действий**
1. Отправитель вызывает `StartStreamTransfer`.
2. Получатель принимает `AcceptStreamTransfer` или отклоняет `RejectStreamTransfer`.
3. Отправитель шлет чанки через `SendStreamChunk`.
4. Получатель проверяет `ChunkHash` и отвечает `AckStreamChunks` или `NackStreamChunks`.
5. При обрыве получатель вызывает `RequestStreamTransferResume` и присылает недостающие `Seq`.
6. Когда файл собран, получатель вызывает `CompleteStreamTransfer`.
7. После `Complete` сервер пишет отчет в основной чат.

## DTO стриминга (кратко)

**StartStreamTransfer**
```json
{
  "streamChatId": "GUID",
  "fileName": "context.txt",
  "fileSize": 123456,
  "fileHash": "sha256-base64",
  "fileHashAlgorithm": "SHA-256",
  "chunkHashAlgorithm": "CRC32",
  "chunkSize": 524288,
  "totalChunks": 1,
  "contentType": "text/plain",
  "caption": "файл для чтения"
}
```

**Chunk**
```json
{
  "transferId": "GUID",
  "seq": 0,
  "data": "bytes",
  "chunkHash": "crc32-base64-or-hex",
  "isLast": true
}
```

## Отчет о передаче (в основной чат)

После `CompleteStreamTransfer` создается системное сообщение:
1. `Kind = "stream_report"`
2. `Content = "Передан файл: {FileName} ({FileSize} байт)"`
3. `MetadataJson` = сериализованный `StreamTransferReportDto`

Пример `MetadataJson`:
```json
{
  "streamChatId": "GUID",
  "senderId": "GUID",
  "receiverId": "GUID",
  "fileName": "context.txt",
  "fileSize": 123456,
  "fileHash": "sha256-base64",
  "status": "completed",
  "chunkSize": 524288,
  "totalChunks": 1,
  "startedAt": "2026-03-24T12:00:00Z",
  "completedAt": "2026-03-24T12:01:00Z"
}
```

## Конфигурация

Раздел `StreamTransfer` в `appsettings.json`:
```json
{
  "ChunkSizeBytes": 524288,
  "WindowSize": 64,
  "MaxFileSizeBytes": 10737418240,
  "SessionTtlMinutes": 60,
  "CleanupIntervalSeconds": 300
}
```

## Примечания по безопасности
1. Сервер не сохраняет файл на диск.
2. Сервер не вычисляет хешы, это ответственность клиента.
3. Рекомендуется хранить `FileHash` в отчете для проверки целостности.
