# Camera Maintainer

Система автоматического обслуживания IP-камер через протокол ONVIF.

**Автоматизация**: обнаружение, мониторинг, авто-восстановление и заявки  
**Интерфейс**: веб-панель с ролевым доступом и обновлениями через SignalR  

## Возможности

### 🤖 Автоматические (без участия человека)
- **Обнаружение**: WS-Discovery и сканирование заданного диапазона IP
- **Мониторинг**: управляемая очередь проверок с отдельным интервалом для каждой камеры
- **Авто-восстановление**: повтор при сетевых сбоях и перезагрузка доступной ONVIF-камеры
- **Бэкапы**: ручные и плановые снимки доступной ONVIF-конфигурации
- **Синхронизация NTP**: авто-коррекция времени при расхождении > 5 сек
- **Circuit Breaker**: авто-пропуск проблемных камер для экономии ресурсов

### 👤 Для менеджеров (только алерты)
- Камера офлайн > 15 мин после авто-восстановления
- Ошибка аутентификации
- Изменение или устаревшая версия прошивки относительно настроенного эталона

### 🎮 Ручное управление (при необходимости)
- PTZ: джойстик, пресеты, absolute/relative move
- Конфигурация: просмотр сети и видео-профилей, управление пользователями, синхронизация времени
- Обслуживание: резервная копия, перезагрузка и сброс к заводским настройкам

## Архитектура

```
CameraMaintainer/
└── src/
    ├── CameraMaintainer.Core/           # Доменные модели и интерфейсы
    ├── CameraMaintainer.Infrastructure/ # EF Core, SQLite, репозитории, шифрование
    ├── CameraMaintainer.Onvif/          # ONVIF SOAP-клиент и WS-Discovery
    ├── CameraMaintainer.Services/       # Бизнес-логика
    ├── CameraMaintainer.Workers/        # Фоновые процессы
    └── CameraMaintainer.Web/            # ASP.NET Core, Razor Pages и SignalR
```

## Технологический стек

| Слой | Технология |
|------|-----------|
| Бэкенд | C# .NET 10, ASP.NET Core |
| ONVIF | Ручной SOAP/XML через HttpClient + WS-Security |
| База данных | SQLite (EF Core, WAL mode) |
| UI | Razor Pages + SignalR (real-time) + Vanilla JS |
| Производительность | Асинхронный .NET, общий лимит параллельных проверок |
| Защита паролей камер | AES-256-GCM |
| Планировщик | BackgroundService (встроенный в .NET) |

## Быстрый старт

### Требования
- .NET 10 SDK
- Windows 10/11 или Windows Server 2019+
- Node.js 20+ (только для запуска браузерных тестов)

### Запуск

```bash
# Клонировать репозиторий
cd CameraMaintainer

# Запустить веб-приложение
dotnet run --project src/CameraMaintainer.Web

# Открыть в браузере
# http://localhost:5100
```

### Конфигурация

Настройки в `src/CameraMaintainer.Web/appsettings.json`:

```jsonc
{
  "Database": { "Path": "" },           // По умолчанию: %LOCALAPPDATA%\\CameraMaintainer\\cameramaintainer.db
  "Encryption": { "Key": "" },          // По умолчанию ключ создаётся рядом с рабочей базой
  "Health": {
    "CycleIntervalSeconds": 60          // Интервал цикла проверки
  },
  "Discovery": { "IntervalMinutes": 5 },  // Интервал авто-обнаружения
  "Urls": "http://0.0.0.0:5100"
}
```

При первом запуске в Production задайте пароль начального администратора через
`CAMERAMAINTAINER_ADMIN_PASSWORD`. Самостоятельная регистрация по умолчанию
доступна только в Development. Для новой Development-базы без заданного пароля
приложение создаёт случайный временный пароль администратора и выводит его в журнал запуска.

### Проверка перед релизом

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Команда восстанавливает зависимости строго по lock-файлам, проверяет их уязвимости
и форматирование, выполняет изолированную Release-сборку с предупреждениями как
ошибками, запускает серверные и браузерные тесты, а также проверяет синтаксис
клиентского JavaScript.

### Публикация Windows x64

```powershell
dotnet restore .\CameraMaintainer.slnx --locked-mode
dotnet publish .\src\CameraMaintainer.Web\CameraMaintainer.Web.csproj `
  -c Release -r win-x64 --self-contained false --no-restore `
  -o .\publish -warnaserror
```

Публикация требует установленный .NET 10 ASP.NET Core Runtime на сервере. Рабочая
база данных и ключ шифрования хранятся вне `publish`, в `%LOCALAPPDATA%\CameraMaintainer`.

## API

| Метод | Путь | Описание |
|-------|------|----------|
| `GET` | `/api/cameras` | Список камер |
| `POST` | `/api/cameras` | Добавить камеру вручную |
| `GET` | `/api/cameras/{id}` | Детали камеры |
| `PATCH` | `/api/cameras/{id}/credentials` | Установить учётные данные |
| `POST` | `/api/discovery/scan` | Запустить сканирование сети |
| `GET` | `/api/cameras/{id}/health` | Статус здоровья камеры |
| `POST` | `/api/cameras/{id}/health/check` | Принудительная проверка |
| `POST` | `/api/health/batch` | Массовая проверка |
| `GET` | `/api/health/status` | Сводка по всем камерам |
| `GET` | `/api/cameras/{id}/ptz/presets` | PTZ пресеты |
| `POST` | `/api/cameras/{id}/ptz/move` | PTZ движение |
| `POST` | `/api/cameras/{id}/ptz/goto` | Перейти к пресету |
| `POST` | `/api/cameras/{id}/ptz/stop` | Остановить PTZ |
| `GET` | `/api/cameras/{id}/config/network` | Сетевые настройки |
| `GET` | `/api/cameras/{id}/config/users` | Список пользователей |
| `POST` | `/api/cameras/{id}/config/sync-time` | Синхронизация времени |
| `POST` | `/api/cameras/{id}/maintenance/backup` | Бэкап конфигурации |
| `POST` | `/api/cameras/{id}/maintenance/reboot` | Перезагрузка камеры |
| `POST` | `/api/cameras/{id}/maintenance/factory-reset` | Сброс к заводским |
| `GET` | `/api/alerts` | Список алертов |
| `GET` | `/api/alerts/{id}` | Карточка алерта |
| `POST` | `/api/alerts/{id}/assign` | Назначить ответственного |
| `POST` | `/api/alerts/{id}/stage` | Изменить этап обработки |
| `POST` | `/api/alerts/{id}/resolve` | Закрыть алерт |

## Производительность

- **Общий предел параллелизма**: две проверки на доступное физическое ядро
- **WS-Discovery**: управляемый UDP-мультикаст на .NET
- **Circuit Breaker**: сбойные камеры пропускаются, не тратят ресурсы
- **WAL-режим SQLite**: конкурентное чтение при записи

## Безопасность

- Пароли камер шифруются AES-256-GCM в базе данных
- Ключ берётся из конфигурации или хранится рядом с рабочей базой данных
- API не возвращает учётные данные камер
- Рекомендуется настроить HTTPS для production

## Лицензия

MIT
