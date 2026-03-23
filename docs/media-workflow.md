# Инструкция по работе с медиа (Upload First, Send Last)

Этот документ описывает, как сейчас работают загрузка файлов и отправка сообщений.

## Цели
- Сообщения с текстом, только медиа или текст + медиа.
- Вложения неизменяемы после отправки сообщения.
- Редактирование текста будет возможно позже (пока не реализовано).

## Модель хранения
Используются две MongoDB коллекции:

1) `Messages`
- Хранит финальные сообщения.
- `Attachments` — список готовых файлов, после отправки не меняется.

2) `MediaUploads`
- Временные загрузки до отправки сообщения.
- Создаются через `init`, обновляются через `complete`, удаляются после `SendMessage`.

## Ключевые структуры
`MessageAttachment` (внутри `Message`):
```json
{
  "id": "attachmentId",
  "objectKey": "conversations/<conversationId>/uploads/<attachmentId>/<fileName>",
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "size": 123456,
  "status": "Ready",
  "createdAt": "2026-03-23T12:00:00Z",
  "encryption": null
}
```

`MediaUpload` (в коллекции `MediaUploads`):
```json
{
  "id": "attachmentId",
  "conversationId": "uuid",
  "userId": "uuid",
  "objectKey": "conversations/<conversationId>/uploads/<attachmentId>/<fileName>",
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "size": 123456,
  "status": "Pending | Ready | Failed",
  "createdAt": "2026-03-23T12:00:00Z",
  "encryption": null
}
```

## API эндпоинты

### 1) Init Upload
`POST /api/media/init`

Запрос:
```json
{
  "conversationId": "uuid",
  "fileName": "photo.jpg",
  "contentType": "image/jpeg",
  "size": 123456
}
```

Ответ:
```json
{
  "attachmentId": "string",
  "uploadUrl": "https://...",
  "expiresAt": "2026-03-23T12:10:00Z"
}
```

Примечания:
- Создаёт запись в `MediaUploads` со статусом `Pending`.
- Возвращает pre-signed PUT URL для S3.

### 2) Upload в S3
`PUT <uploadUrl>`

Примечания:
- Используй тот же `Content-Type`, что был в `init`.

### 3) Complete Upload
`POST /api/media/complete`

Запрос:
```json
{
  "conversationId": "uuid",
  "attachmentId": "string"
}
```

Ответ: `204 NoContent`

Примечания:
- Сервер проверяет, что файл реально есть в S3.
- Статус становится `Ready` или `Failed`.

### 4) Send Message
`POST /api/messages`

Запрос:
```json
{
  "conversationId": "uuid",
  "content": "привет",
  "attachmentIds": ["id1", "id2"]
}
```

Правила:
- Если `content` пустой, `attachmentIds` должен быть непустым.
- Все `attachmentIds` должны быть `Ready` и принадлежать пользователю и беседе.
- После отправки соответствующие `MediaUploads` удаляются.

### 5) Download Media
`GET /api/media/{conversationId}/{messageId}/{attachmentId}/url`

Ответ:
```json
{
  "url": "https://...",
  "expiresAt": "2026-03-23T12:05:00Z"
}
```

## Клиентские сценарии

### Только текст
1. `POST /api/messages` с `content`, `attachmentIds` пустой.

### Только медиа
1. `POST /api/media/init`
2. `PUT` в S3
3. `POST /api/media/complete`
4. `POST /api/messages` с `attachmentIds` и пустым `content`

### Текст + медиа
1. `POST /api/media/init` для каждого файла
2. `PUT` в S3
3. `POST /api/media/complete`
4. `POST /api/messages` с `content` и `attachmentIds`

## Правила неизменяемости
- Вложения фиксируются в момент отправки.
- Вложения нельзя добавлять, удалять или заменять после отправки.
- В будущем можно редактировать только текст сообщения.

## Настройки S3
Берутся из `appsettings.json` раздел `S3`:
- `ServiceURL`
- `BucketName`
- `Region`
- `AccessKey`
- `SecretKey`

TTL для pre-signed URL:
- Upload: 10 минут
- Download: 5 минут

## Типовые ошибки
- `401/403`: пользователь не участник беседы.
- `404`: загрузка или сообщение не найдены.
- `400`: загрузка не готова или неверный запрос.
