(function () {
    'use strict';

    const storageKey = 'cm_language';
    const supportedLanguages = new Set(['ru', 'uz-Latn', 'uz-Cyrl']);
    const languageLabels = {
        ru: 'Русский',
        'uz-Latn': 'O‘zbekcha',
        'uz-Cyrl': 'Ўзбекча'
    };

    const uzLatin = Object.freeze({
        'Camera Maintainer | Контроль и проверка камер': 'Camera Maintainer | Kameralarni nazorat qilish va tekshirish',
        'Центр мониторинга камер': 'Kameralarni monitoring qilish markazi',
        'Контроль и проверка камер': 'Kameralarni nazorat qilish va tekshirish',
        'Контроль и проверка камер.': 'Kameralarni nazorat qilish va tekshirish.',
        'Главное меню': 'Asosiy menyu',
        'Камеры': 'Kameralar',
        'Камера': 'Kamera',
        'Все камеры': 'Barcha kameralar',
        'Ошибки': 'Xatolar',
        'Ошибка': 'Xato',
        'Все ошибки': 'Barcha xatolar',
        'Активные': 'Faol',
        'История': 'Tarix',
        'История ошибок': 'Xatolar tarixi',
        'История ошибок камеры': 'Kamera xatolari tarixi',
        'История по камерам': 'Kameralar bo‘yicha tarix',
        'Настройки': 'Sozlamalar',
        'Доступы': 'Ruxsatlar',
        'Обзор': 'Umumiy ko‘rinish',
        'Мониторинг': 'Monitoring',
        'Обслуживание': 'Xizmat ko‘rsatish',
        'Пользователи': 'Foydalanuvchilar',
        'Пользователь': 'Foydalanuvchi',
        'Уведомления': 'Bildirishnomalar',
        'Конфигурация': 'Konfiguratsiya',
        'Администрирование': 'Boshqaruv',
        'Выйти': 'Chiqish',
        'Выйти из аккаунта': 'Akkauntdan chiqish',
        'Открыть или закрыть меню': 'Menyuni ochish yoki yopish',
        'Включить тёмную тему': 'Qorong‘i mavzuni yoqish',
        'Включить светлую тему': 'Yorug‘ mavzuni yoqish',
        'Язык интерфейса': 'Interfeys tili',
        'Состояние системы': 'Tizim holati',
        'Нагрузка сервера': 'Server yuklamasi',
        'Показатели нагрузки сервера': 'Server yuklamasi ko‘rsatkichlari',
        'Процессор': 'Protsessor',
        'Процессор системы': 'Tizim protsessori',
        'Оперативная память': 'Tezkor xotira',
        'Приложение': 'Ilova',
        'Память приложения': 'Ilova xotirasi',
        'Потоки приложения': 'Ilova oqimlari',
        'Время работы': 'Ishlash vaqti',
        'Данные временно недоступны': 'Ma’lumotlar vaqtincha mavjud emas',
        'Время сервера и NTP Ташкента': 'Server va Toshkent NTP vaqti',
        'ТАШКЕНТ': 'TOSHKENT',
        'получение времени...': 'vaqt olinmoqda...',
        'Получение данных...': 'Ma’lumotlar olinmoqda...',
        'Обновлено': 'Yangilandi',

        'Вход': 'Kirish',
        'Войти в систему': 'Tizimga kirish',
        'Регистрация': 'Ro‘yxatdan o‘tish',
        'Имя пользователя': 'Foydalanuvchi nomi',
        'Пароль': 'Parol',
        'Имя': 'Ism',
        'Как к вам обращаться': 'Sizga qanday murojaat qilamiz',
        'Создать аккаунт': 'Akkaunt yaratish',
        'Аккаунт создан. Теперь выполните вход.': 'Akkaunt yaratildi. Endi tizimga kiring.',
        'Новый аккаунт получает доступ только к просмотру камер. Расширенные права выдаёт администратор.': 'Yangi akkaunt faqat kameralarni ko‘rish huquqini oladi. Kengaytirilgan huquqlarni administrator beradi.',
        'Слишком много попыток входа. Подождите одну минуту и повторите.': 'Kirish urinishlari juda ko‘p. Bir daqiqa kutib, qayta urinib ko‘ring.',
        'Операция не выполнена': 'Amal bajarilmadi',
        'Неверное имя пользователя или пароль': 'Foydalanuvchi nomi yoki parol noto‘g‘ri',

        'Онлайн': 'Onlayn',
        'Офлайн': 'Oflayn',
        'В сети': 'Tarmoqda',
        'Нет связи': 'Aloqa yo‘q',
        'Неизвестно': 'Noma’lum',
        'Активен': 'Faol',
        'Заблокирован': 'Bloklangan',
        'Аккаунт активен': 'Akkaunt faol',
        'Разрешить вход в систему': 'Tizimga kirishga ruxsat berish',
        'Статус': 'Holat',
        'Состояние': 'Holat',
        'Текущий статус': 'Joriy holat',
        'Статус камеры': 'Kamera holati',
        'Актуальное состояние камеры': 'Kameraning joriy holati',
        'В рабочем состоянии': 'Ish holatida',
        'Недоступен': 'Mavjud emas',
        'Нет данных': 'Ma’lumot yo‘q',
        'Не указано': 'Ko‘rsatilmagan',
        'Не указан': 'Ko‘rsatilmagan',
        'Не указана': 'Ko‘rsatilmagan',
        'Никогда': 'Hech qachon',
        'Система': 'Tizim',
        'система': 'tizim',
        'системное действие': 'tizim amali',
        'Системная ошибка': 'Tizim xatosi',
        'Системные ошибки': 'Tizim xatolari',

        'Добавить': 'Qo‘shish',
        'Добавить камеру': 'Kamera qo‘shish',
        'Добавить аккаунт': 'Akkaunt qo‘shish',
        'Добавить колонку': 'Ustun qo‘shish',
        'Добавить в проверки': 'Tekshiruvlarga qo‘shish',
        'Удалить': 'O‘chirish',
        'Изменить': 'O‘zgartirish',
        'Настроить': 'Sozlash',
        'Сохранить': 'Saqlash',
        'Сохранить и закрыть': 'Saqlash va yopish',
        'Сохранить настройки': 'Sozlamalarni saqlash',
        'Сохранить пароль': 'Parolni saqlash',
        'Сохранить уведомления': 'Bildirishnomalarni saqlash',
        'Сохраняем...': 'Saqlanmoqda...',
        'Закрыть': 'Yopish',
        'Закрыть календарь': 'Kalendarni yopish',
        'Отмена': 'Bekor qilish',
        'Назад': 'Orqaga',
        '← Вернуться к списку': '← Ro‘yxatga qaytish',
        'К списку камер': 'Kameralar ro‘yxatiga',
        'Обновить': 'Yangilash',
        'Найти': 'Qidirish',
        'Открыть': 'Ochish',
        'Открыть карточку': 'Kartochkani ochish',
        'Открыть заявку': 'Arizani ochish',
        'Открыть на карте': 'Xaritada ochish',
        'Раскрыть подробности': 'Tafsilotlarni ochish',
        'Повторить': 'Takrorlash',
        'Повторить загрузку': 'Yuklashni takrorlash',
        'Сбросить': 'Tiklash',
        'Очистить сейчас': 'Hozir tozalash',
        'Выбрать все': 'Barchasini tanlash',
        'Проверить': 'Tekshirish',
        'Проверить сейчас': 'Hozir tekshirish',
        'Проверить выбранные': 'Tanlanganlarni tekshirish',
        'Проверить звук': 'Ovozni tekshirish',
        'Переместить': 'Ko‘chirish',
        'Переместить выше': 'Yuqoriga ko‘chirish',
        'Переместить ниже': 'Pastga ko‘chirish',
        'Назначить себе': 'O‘zimga tayinlash',
        'Назначить себя': 'O‘zimni tayinlash',
        'Передать': 'Topshirish',
        'Передать дальше': 'Keyingiga topshirish',
        'Передать сотруднику': 'Xodimga topshirish',
        'Принять': 'Qabul qilish',
        'Вернуть': 'Qaytarish',
        'Завершить': 'Yakunlash',
        'В работу': 'Ishga olish',
        'Стоп': 'To‘xtatish',

        'Список устройств': 'Qurilmalar ro‘yxati',
        'устройств': 'qurilma',
        'Камеры не найдены': 'Kameralar topilmadi',
        'Камера не найдена': 'Kamera topilmadi',
        'Добавьте камеру вручную или измените параметры поиска.': 'Kamerani qo‘lda qo‘shing yoki qidiruv parametrlarini o‘zgartiring.',
        'Все камеры участвуют в автоматическом цикле проверок.': 'Barcha kameralar avtomatik tekshiruv siklida qatnashmoqda.',
        'Исключённых камер нет': 'Tekshiruvdan chiqarilgan kameralar yo‘q',
        'Поиск по имени, IP или производителю': 'Nomi, IP manzili yoki ishlab chiqaruvchi bo‘yicha qidirish',
        'Все состояния': 'Barcha holatlar',
        'Только исключённые': 'Faqat chiqarilganlar',
        'Сортировка по ID': 'ID bo‘yicha saralash',
        'Выбрать состояние обработки': 'Ishlov berish holatini tanlang',
        'Фильтр камер': 'Kameralar filtri',
        'Всего камер': 'Jami kameralar',
        'В цикле проверок': 'Tekshiruv siklida',
        'Исключена из проверок': 'Tekshiruvlardan chiqarilgan',
        'Исключить из проверок': 'Tekshiruvlardan chiqarish',
        'Камера в цикле': 'Kamera siklda',
        'Камера исключена из цикла': 'Kamera sikldan chiqarilgan',
        'Автоматические проверки': 'Avtomatik tekshiruvlar',
        'Проверки камер': 'Kameralarni tekshirish',
        'Проверка камер': 'Kameralarni tekshirish',
        'Последняя проверка': 'Oxirgi tekshiruv',
        'Последняя активность': 'Oxirgi faollik',
        'Последовательных сбоев': 'Ketma-ket nosozliklar',
        'Интервал проверки': 'Tekshiruv oralig‘i',
        'Интервал проверки, сек': 'Tekshiruv oralig‘i, soniya',
        'Строгий порядок по ID': 'ID bo‘yicha qat’iy tartib',
        'Проверять стабильные камеры': 'Barqaror kameralarni tekshirish',
        'Проверять стабильные камеры каждый N-й проход очереди': 'Barqaror kameralarni navbatning har N-o‘tishida tekshirish',
        'Проверок подряд без ошибок до признания стабильной': 'Barqaror deb topish uchun ketma-ket xatosiz tekshiruvlar',

        'Данные выбранной камеры': 'Tanlangan kamera ma’lumotlari',
        'Данные камеры': 'Kamera ma’lumotlari',
        'Информация об устройстве': 'Qurilma haqida ma’lumot',
        'Название': 'Nomi',
        'Название:': 'Nomi:',
        'Модель': 'Model',
        'Производитель': 'Ishlab chiqaruvchi',
        'Производитель / модель': 'Ishlab chiqaruvchi / model',
        'IP-адрес': 'IP manzil',
        'MAC-адрес': 'MAC manzil',
        'Серийный номер': 'Seriya raqami',
        'Прошивка': 'Proshivka',
        'Сервисный URL': 'Xizmat URL manzili',
        'Порт': 'Port',
        'Возможности': 'Imkoniyatlar',
        'События': 'Hodisalar',
        'Поддерживаются': 'Qo‘llab-quvvatlanadi',
        'Не поддерживаются': 'Qo‘llab-quvvatlanmaydi',
        'Поддерживается': 'Qo‘llab-quvvatlanadi',
        'Не поддерживается': 'Qo‘llab-quvvatlanmaydi',
        'Локация': 'Joylashuv',
        'Локация не указана': 'Joylashuv ko‘rsatilmagan',
        'Улица не указана': 'Ko‘cha ko‘rsatilmagan',
        'Улица / адрес': 'Ko‘cha / manzil',
        'Адрес': 'Manzil',
        'Широта': 'Kenglik',
        'Долгота': 'Uzunlik',
        'Широта, долгота': 'Kenglik, uzunlik',
        'Координаты не указаны': 'Koordinatalar ko‘rsatilmagan',
        'Определить улицу': 'Ko‘chani aniqlash',
        'Отправить координаты в картографический сервис': 'Koordinatalarni xarita xizmatiga yuborish',
        'Адресные данные © OpenStreetMap': 'Manzil ma’lumotlari © OpenStreetMap',
        'Сеть': 'Tarmoq',
        'Сеть и доступность': 'Tarmoq va mavjudlik',
        'Подключение': 'Ulanish',
        'Пинг': 'Ping',
        'Время отклика': 'Javob vaqti',
        'Время ответа сети (пинг)': 'Tarmoq javob vaqti (ping)',
        'Потери пакетов': 'Paketlar yo‘qotilishi',
        'Потери проверочных пакетов': 'Tekshiruv paketlari yo‘qotilishi',
        'Колебания задержки (джиттер)': 'Kechikish tebranishi (jitter)',
        'Джиттер': 'Jitter',
        'Ближе к 0 мс': '0 ms ga yaqinroq',
        'Меньше — лучше': 'Kamroq bo‘lsa yaxshi',

        'Показатели диагностики': 'Diagnostika ko‘rsatkichlari',
        'Диагностика камеры': 'Kamera diagnostikasi',
        'Технические показатели': 'Texnik ko‘rsatkichlar',
        'Метрики кадра': 'Kadr ko‘rsatkichlari',
        'Последний кадр': 'Oxirgi kadr',
        'Последний кадр ONVIF': 'Oxirgi ONVIF kadri',
        'Последний кадр камеры': 'Kameraning oxirgi kadri',
        'Живой кадр': 'Jonli kadr',
        'Кадр ONVIF': 'ONVIF kadri',
        'Кадр не сохранён': 'Kadr saqlanmagan',
        'Нет снимка': 'Tasvir yo‘q',
        'Не удалось загрузить кадр': 'Kadrni yuklab bo‘lmadi',
        'Получение последнего кадра...': 'Oxirgi kadr olinmoqda...',
        'Ожидание кадра': 'Kadr kutilmoqda',
        'Двойной клик или нажатие для увеличения': 'Kattalashtirish uchun ikki marta bosing yoki tanlang',
        'Увеличенный кадр': 'Kattalashtirilgan kadr',
        'Оценка изображения': 'Tasvir bahosi',
        'Показатели в норме': 'Ko‘rsatkichlar me’yorda',
        'Норма / ориентир': 'Me’yor / mo‘ljal',
        'Сейчас': 'Hozir',
        'Яркость': 'Yorqinlik',
        'Средняя яркость': 'O‘rtacha yorqinlik',
        'Контраст': 'Kontrast',
        'Контраст изображения': 'Tasvir kontrasti',
        'Энтропия': 'Entropiya',
        'Разнообразие яркости (энтропия)': 'Yorqinlik xilma-xilligi (entropiya)',
        'Контуры': 'Konturlar',
        'Доля контуров': 'Konturlar ulushi',
        'Плотность граней': 'Chegaralar zichligi',
        'Однородность текстуры': 'Tekstura bir xilligi',
        'Дисперсия': 'Dispersiya',
        'Размер кадра': 'Kadr hajmi',
        'Время загрузки кадра': 'Kadrni yuklash vaqti',
        'Расчёт по пикселям...': 'Piksellar bo‘yicha hisoblanmoqda...',
        'История анализа кадров': 'Kadrlar tahlili tarixi',
        'История появится после следующей успешной проверки кадра.': 'Tarix keyingi muvaffaqiyatli kadr tekshiruvidan so‘ng paydo bo‘ladi.',
        'Нормальные проверки хранят только показатели. Снимок остаётся на диске при отклонении.': 'Me’yordagi tekshiruvlarda faqat ko‘rsatkichlar saqlanadi. Og‘ish bo‘lsa, tasvir diskda qoladi.',
        'В норме сохраняются только показатели. Изображение записывается на диск при отклонении.': 'Me’yorda faqat ko‘rsatkichlar saqlanadi. Og‘ish bo‘lsa, tasvir diskka yoziladi.',
        'Норма': 'Me’yor',
        'Отклонение': 'Og‘ish',

        'Обработка заявок': 'Arizalarni qayta ishlash',
        'Очередь ошибок': 'Xatolar navbati',
        'Доска активных ошибок': 'Faol xatolar doskasi',
        'Фильтры ошибок': 'Xatolar filtrlari',
        'Все типы': 'Barcha turlar',
        'Вся очередь': 'Butun navbat',
        'Карточек': 'Kartochkalar',
        'Всего': 'Jami',
        'в очереди': 'navbatda',
        'в истории': 'tarixda',
        'есть исполнитель': 'ijrochi bor',
        'нужен ответственный': 'mas’ul kerak',
        'Активных ошибок нет': 'Faol xatolar yo‘q',
        'История пока пуста': 'Tarix hozircha bo‘sh',
        'Нет заявок на этом этапе': 'Bu bosqichda arizalar yo‘q',
        'Этапы очереди не настроены.': 'Navbat bosqichlari sozlanmagan.',
        'Для вашей роли этапы не настроены': 'Sizning rolingiz uchun bosqichlar sozlanmagan',
        'Обратитесь к администратору, чтобы получить доступ к колонкам очереди.': 'Navbat ustunlariga ruxsat olish uchun administratorga murojaat qiling.',
        'Новые': 'Yangi',
        'Не назначены': 'Tayinlanmagan',
        'Назначено технику': 'Texnikka tayinlangan',
        'В работе': 'Ishda',
        'Ожидает уточнения': 'Aniqlik kutilmoqda',
        'На проверке': 'Tekshiruvda',
        'Готово к закрытию': 'Yopishga tayyor',
        'Закрыты': 'Yopilgan',
        'Закрыта': 'Yopilgan',
        'Новые карточки и назначения': 'Yangi kartochkalar va tayinlovlar',
        'Карточка ожидает назначения': 'Kartochka tayinlovni kutmoqda',
        'Техник должен принять задачу': 'Texnik vazifani qabul qilishi kerak',
        'Техник выполняет работы': 'Texnik ishni bajarmoqda',
        'Нужны данные, доступ или ответ': 'Ma’lumot, ruxsat yoki javob kerak',
        'Техник завершил, менеджер проверяет': 'Texnik tugatdi, menejer tekshirmoqda',
        'Результат проверен': 'Natija tekshirildi',
        'Техническая': 'Texnik',
        'Технические': 'Texnik',
        'Физическая': 'Jismoniy',
        'Физические': 'Jismoniy',
        'Тип': 'Tur',
        'Тип неисправности': 'Nosozlik turi',
        'Выбрать тип неисправности': 'Nosozlik turini tanlang',
        'Предупреждение': 'Ogohlantirish',
        'Критическая': 'Jiddiy',
        'Информационная': 'Axborot',
        'Обычная': 'Oddiy',
        'Важная': 'Muhim',
        'Срочная': 'Shoshilinch',
        'Ошибка обнаружена': 'Xato aniqlandi',
        'Название ошибки': 'Xato nomi',
        'Начало ошибки': 'Xato boshlangan vaqt',
        'Конец ошибки': 'Xato tugagan vaqt',
        'Начало:': 'Boshlanishi:',
        'Конец:': 'Tugashi:',
        'Обнаружена': 'Aniqlangan',
        'Создана': 'Yaratilgan',
        'Закрыл': 'Yopgan',
        'Исправил:': 'Tuzatgan:',
        'Ответственный': 'Mas’ul',
        'Ответственный менеджер': 'Mas’ul menejer',
        'Ответственный не назначен': 'Mas’ul tayinlanmagan',
        'Не назначен': 'Tayinlanmagan',
        'Назначена': 'Tayinlangan',
        'Не назначена': 'Tayinlanmagan',
        'Назначение ответственного': 'Mas’ulni tayinlash',
        'Этап обработки:': 'Ishlov berish bosqichi:',
        'Текущий этап': 'Joriy bosqich',
        'Этап проверки': 'Tekshiruv bosqichi',
        'Причина': 'Sabab',
        'Время проверки': 'Tekshiruv vaqti',
        'Результат следующей проверки': 'Keyingi tekshiruv natijasi',
        'Ошибка активна': 'Xato faol',
        'Неисправности': 'Nosozliklar',
        'Неисправности камеры': 'Kamera nosozliklari',
        'Данные заявки': 'Ariza ma’lumotlari',
        'Заявка': 'Ariza',
        'Работа с заявкой': 'Ariza bilan ishlash',
        'Работа с ошибкой': 'Xato bilan ishlash',
        'Выполнение работ': 'Ishlarni bajarish',
        'Рабочие действия': 'Ish amallari',
        'Ход работы': 'Ish jarayoni',
        'Что требуется сделать': 'Nima qilish kerak',
        'Как проведена работа': 'Ish qanday bajarildi',
        'Опишите диагностику, выполненные действия и результат': 'Diagnostika, bajarilgan ishlar va natijani yozing',
        'Опишите результат диагностики, задачу и ожидаемый результат': 'Diagnostika natijasi, vazifa va kutilgan natijani yozing',
        'Отчёт о работе': 'Ish hisoboti',
        'Отчёт о работе заполнен': 'Ish hisoboti to‘ldirilgan',
        'Отчёт пока не заполнен': 'Hisobot hali to‘ldirilmagan',
        'Отчёт обязателен при закрытии ошибки.': 'Xatoni yopishda hisobot majburiy.',
        'Действия по ошибке ещё не записаны.': 'Xato bo‘yicha amallar hali yozilmagan.',
        'История изменений': 'O‘zgarishlar tarixi',
        'Записей об изменениях пока нет': 'O‘zgarishlar haqida yozuvlar hozircha yo‘q',
        'Загрузка истории...': 'Tarix yuklanmoqda...',
        'Загрузка карточки...': 'Kartochka yuklanmoqda...',
        'Не удалось открыть карточку': 'Kartochkani ochib bo‘lmadi',
        'Серийный номер ошибки': 'Xato seriya raqami',
        'Серийный номер карточки': 'Kartochka seriya raqami',

        'Передача работы': 'Ishni topshirish',
        'Передача работы между сотрудниками': 'Ishni xodimlar o‘rtasida topshirish',
        'Работа пока не передавалась другим сотрудникам': 'Ish hali boshqa xodimlarga topshirilmagan',
        'Работа передана на проверку': 'Ish tekshiruvga topshirildi',
        'Загрузка передач...': 'Topshiriqlar yuklanmoqda...',
        'Внутренний статус / причина': 'Ichki holat / sabab',
        'Внутренние статусы передачи': 'Topshirishning ichki holatlari',
        'Причины и состояния внутри карточки, без создания новых колонок': 'Yangi ustun yaratmasdan kartochka ichidagi sabab va holatlar',
        'Передал': 'Topshirgan',
        'Получатель': 'Qabul qiluvchi',
        'При принятии:': 'Qabul qilishda:',
        'Срок принятия, часов': 'Qabul qilish muddati, soat',
        'Срок принятия передачи, часов': 'Topshiriqni qabul qilish muddati, soat',
        'После этого срока передача будет отмечена как требующая внимания': 'Bu muddatdan so‘ng topshiriq e’tibor talab qiluvchi deb belgilanadi',

        'Заметки': 'Eslatmalar',
        'Заметки и календарь': 'Eslatmalar va kalendar',
        'Календарь заметок': 'Eslatmalar kalendari',
        'Заметки выбранного дня': 'Tanlangan kun eslatmalari',
        'Ваши заметки': 'Sizning eslatmalaringiz',
        'Нет заметок': 'Eslatmalar yo‘q',
        'Нет активных заметок': 'Faol eslatmalar yo‘q',
        'На этот день записей нет': 'Bu kun uchun yozuv yo‘q',
        'Сегодня': 'Bugun',
        'Предыдущий месяц': 'Oldingi oy',
        'Следующий месяц': 'Keyingi oy',
        'Навигация по календарю': 'Kalendar bo‘yicha navigatsiya',
        'Время:': 'Vaqt:',
        'Цвет:': 'Rang:',
        'Заметка:': 'Eslatma:',
        'Кому:': 'Kimga:',
        '· Кому:': '· Kimga:',
        'Только мне': 'Faqat menga',
        'Всем аккаунтам': 'Barcha akkauntlarga',
        'Только автору': 'Faqat muallifga',
        'Выбранному аккаунту': 'Tanlangan akkauntga',
        'Автор:': 'Muallif:',
        'Выполнил:': 'Bajargan:',
        '· Выполнил:': '· Bajargan:',
        'Добавить заметку': 'Eslatma qo‘shish',
        'Изменить заметку': 'Eslatmani o‘zgartirish',
        'Удалить заметку': 'Eslatmani o‘chirish',
        'Заметка будет удалена из календаря.': 'Eslatma kalendardan o‘chiriladi.',
        'Заметка добавлена': 'Eslatma qo‘shildi',
        'Заметка обновлена': 'Eslatma yangilandi',
        'Заметка удалена': 'Eslatma o‘chirildi',
        'Отметить выполненной': 'Bajarilgan deb belgilash',
        'Вернуть в работу': 'Ishga qaytarish',
        'Просрочено': 'Muddati o‘tgan',

        'Настроить этапы': 'Bosqichlarni sozlash',
        'Этапы обработки ошибки': 'Xatoni qayta ishlash bosqichlari',
        'Этапы очереди ошибок': 'Xatolar navbati bosqichlari',
        'Настройка колонок ошибок': 'Xato ustunlarini sozlash',
        'Новая колонка': 'Yangi ustun',
        'Краткое описание этапа': 'Bosqichning qisqa tavsifi',
        'Показывать для типов аккаунтов': 'Akkaunt turlari uchun ko‘rsatish',
        'Системный этап: доступно переименование и настройка': 'Tizim bosqichi: nomini va sozlamalarini o‘zgartirish mumkin',
        'Доступ аккаунта дополнительно ограничивается выбранной ролью.': 'Akkaunt ruxsati tanlangan rol bilan qo‘shimcha cheklanadi.',

        'Аккаунты, роли и видимость разделов системы.': 'Akkauntlar, rollar va tizim bo‘limlarining ko‘rinishi.',
        'Добавление, настройка и удаление устройств': 'Qurilmalarni qo‘shish, sozlash va o‘chirish',
        'Текущий аккаунт': 'Joriy akkaunt',
        'Аккаунт': 'Akkaunt',
        'Новый аккаунт': 'Yangi akkaunt',
        'Изменить аккаунт': 'Akkauntni o‘zgartirish',
        'Отображаемое имя': 'Ko‘rsatiladigan ism',
        'Роль': 'Rol',
        'Роли': 'Rollar',
        'Администратор': 'Administrator',
        'Оператор': 'Operator',
        'Техник': 'Texnik',
        'Наблюдатель': 'Kuzatuvchi',
        'Готовые наборы прав, которые можно уточнить вручную.': 'Qo‘lda aniqlashtirish mumkin bo‘lgan tayyor huquqlar to‘plami.',
        'Индивидуальный доступ': 'Shaxsiy ruxsat',
        'Доступ к разделам': 'Bo‘limlarga ruxsat',
        'Разделы': 'Bo‘limlar',
        'видимых разделов': 'ko‘rinadigan bo‘lim',
        'Запускать проверки': 'Tekshiruvlarni ishga tushirish',
        'Управлять камерами': 'Kameralarni boshqarish',
        'Только просмотр': 'Faqat ko‘rish',
        'Разрешены': 'Ruxsat berilgan',
        'Данные назначаются администратором системы': 'Ma’lumotlarni tizim administratori belgilaydi',
        'После сохранения остальные сеансы аккаунта будут завершены': 'Saqlangandan so‘ng akkauntning boshqa seanslari yakunlanadi',
        'Минимум 8 символов': 'Kamida 8 ta belgi',
        'Текущий пароль': 'Joriy parol',
        'Новый пароль': 'Yangi parol',
        'Повторите новый пароль': 'Yangi parolni takrorlang',
        'Смена пароля': 'Parolni almashtirish',
        'Данные профиля и смена пароля': 'Profil ma’lumotlari va parolni almashtirish',
        'Личные параметры аккаунта и доступные системные настройки.': 'Akkauntning shaxsiy parametrlari va mavjud tizim sozlamalari.',

        'Настроить уведомления': 'Bildirishnomalarni sozlash',
        'Уведомления аккаунта': 'Akkaunt bildirishnomalari',
        'Новые уведомления': 'Yangi bildirishnomalar',
        'Просмотренные': 'Ko‘rilgan',
        'Нет новых событий': 'Yangi hodisalar yo‘q',
        'Звуковой сигнал': 'Ovozli signal',
        'Всплывающее уведомление': 'Qalqib chiquvchi bildirishnoma',
        'Типы уведомлений': 'Bildirishnoma turlari',
        'Разделы уведомлений': 'Bildirishnoma bo‘limlari',
        'Сроки уведомлений': 'Bildirishnoma muddatlari',
        'Выберите события, которые должны появляться у этого аккаунта': 'Ushbu akkauntga ko‘rsatiladigan hodisalarni tanlang',
        'Настройка сохраняется отдельно для текущего аккаунта': 'Sozlama joriy akkaunt uchun alohida saqlanadi',
        'Интервалы применяются отдельно для текущего аккаунта': 'Oraliqlar joriy akkaunt uchun alohida qo‘llanadi',
        'Просмотренные, но не закрытые': 'Ko‘rilgan, ammo yopilmagan',
        'Новая передача, принятие, возврат, завершение и просрочка': 'Yangi topshiriq, qabul qilish, qaytarish, yakunlash va muddatdan o‘tish',
        'Напоминание за сутки до события и при просрочке': 'Hodisadan bir kun oldin va muddati o‘tganda eslatish',
        'Напоминание ответственному и администратору': 'Mas’ul va administratorga eslatish',

        'Параметры системы': 'Tizim parametrlari',
        'Хранение данных': 'Ma’lumotlarni saqlash',
        'Политика хранения ошибок': 'Xatolarni saqlash siyosati',
        'Правила очистки': 'Tozalash qoidalari',
        'Срок хранения истории закрытых ошибок': 'Yopilgan xatolar tarixini saqlash muddati',
        'Срок рассчитывается календарными месяцами': 'Muddat kalendar oylari bo‘yicha hisoblanadi',
        'Активные ошибки не удаляются': 'Faol xatolar o‘chirilmaydi',
        'Закрытые записи удаляются постепенно по мере окончания их срока': 'Yopilgan yozuvlar muddati tugashi bilan bosqichma-bosqich o‘chiriladi',
        'Освобождённое место используется новыми записями': 'Bo‘shatilgan joy yangi yozuvlar uchun ishlatiladi',
        'Хранить историю': 'Tarixni saqlash',
        'Хранить историю анализа': 'Tahlil tarixini saqlash',
        'Последних записей на камеру': 'Har bir kamera uchun oxirgi yozuvlar',
        'Сохранять изображения с отклонениями': 'Og‘ishli tasvirlarni saqlash',
        'Состояние хранилища': 'Saqlash joyi holati',
        'Старейшая запись': 'Eng eski yozuv',
        'Граница хранения': 'Saqlash chegarasi',
        'Последняя очистка': 'Oxirgi tozalash',
        'Удалено за запуск': 'Bir ishga tushirishda o‘chirildi',
        'К удалению сейчас: 0': 'Hozir o‘chiriladigan: 0',

        'Автоматические действия': 'Avtomatik amallar',
        'Авто-восстановление': 'Avtomatik tiklash',
        'Авто-резервирование': 'Avtomatik zaxiralash',
        'Авто-резервное копирование': 'Avtomatik zaxira nusxasi',
        'Резервное копирование конфигурации': 'Konfiguratsiyaning zaxira nusxasi',
        'Резервные копии конфигурации': 'Konfiguratsiya zaxira nusxalari',
        'Включено': 'Yoqilgan',
        'Отключено': 'O‘chirilgan',
        'Включить': 'Yoqish',
        'Отключить': 'O‘chirish',
        'Авто-настройки': 'Avtomatik sozlamalar',
        'Перезагрузка': 'Qayta yuklash',
        'Сброс к заводским настройкам': 'Zavod sozlamalariga qaytarish',
        'Синхронизация времени': 'Vaqtni sinxronlash',
        'Синхронизировать время': 'Vaqtni sinxronlash',
        'Синхронизирует время камеры с сервером NTP.': 'Kamera vaqtini NTP serveri bilan sinxronlaydi.',
        'Время синхронизировано': 'Vaqt sinxronlandi',
        'Дата и время камеры': 'Kamera sanasi va vaqti',
        'Расхождение времени': 'Vaqt farqi',

        'PTZ Управление': 'PTZ boshqaruvi',
        'Джойстик управления': 'Boshqaruv joystigi',
        'Направление': 'Yo‘nalish',
        'Вверх': 'Yuqoriga',
        'Вниз': 'Pastga',
        'Влево': 'Chapga',
        'Вправо': 'O‘ngga',
        'Вверх-влево': 'Yuqori-chapga',
        'Вверх-вправо': 'Yuqori-o‘ngga',
        'Вниз-влево': 'Pastki-chapga',
        'Вниз-вправо': 'Pastki-o‘ngga',
        'Приблизить': 'Yaqinlashtirish',
        'Отдалить': 'Uzoqlashtirish',
        'Зум': 'Masshtab',
        'Скорость:': 'Tezlik:',
        'Предустановки (пресеты)': 'Oldindan o‘rnatilgan holatlar (presetlar)',
        'Загрузка пресетов...': 'Presetlar yuklanmoqda...',
        'Сохранить текущую позицию': 'Joriy holatni saqlash',
        'PTZ не поддерживается этой камерой.': 'Bu kamera PTZ’ni qo‘llab-quvvatlamaydi.',

        'Пн': 'Du',
        'Вт': 'Se',
        'Ср': 'Ch',
        'Чт': 'Pa',
        'Пт': 'Ju',
        'Сб': 'Sh',
        'Вс': 'Ya',
        '1 месяц': '1 oy',
        '3 месяца': '3 oy',
        '6 месяцев': '6 oy',
        '1 год': '1 yil',
        '2 года': '2 yil',
        'Без ограничения': 'Cheklanmagan',
        'На странице': 'Sahifada',
        'Предыдущая страница': 'Oldingi sahifa',
        'Следующая страница': 'Keyingi sahifa',
        'Загрузка...': 'Yuklanmoqda...',

        'Камера недоступна': 'Kamera mavjud emas',
        'Камера не ответила вовремя': 'Kamera o‘z vaqtida javob bermadi',
        'Не удалось подключиться к камере': 'Kameraga ulanib bo‘lmadi',
        'Камера отклонила логин или пароль': 'Kamera login yoki parolni rad etdi',
        'ONVIF-служба камеры не найдена': 'Kameraning ONVIF xizmati topilmadi',
        'Камера вернула ошибку HTTP': 'Kamera HTTP xatosini qaytardi',
        'Камера вернула ошибку ONVIF': 'Kamera ONVIF xatosini qaytardi',
        'Повышенная температура камеры': 'Kamera harorati yuqori',
        'Требуется обновление прошивки': 'Proshivkani yangilash kerak',
        'Ошибка авторизации камеры': 'Kamera avtorizatsiyasi xatosi',
        'Изменились настройки камеры': 'Kamera sozlamalari o‘zgardi',
        'Автоматическое восстановление не выполнено': 'Avtomatik tiklash bajarilmadi',
        'Проблема с объективом камеры': 'Kamera obyektivi bilan muammo',
        'Ошибка камеры': 'Kamera xatosi',
        'Ошибка проверки камеры': 'Kamerani tekshirish xatosi',
        'Превышено время ожидания ответа камеры. Проверка не завершилась за отведённое время.': 'Kamera javobini kutish vaqti tugadi. Tekshiruv ajratilgan vaqtda yakunlanmadi.',
        'Камера не ответила за отведённое время': 'Kamera ajratilgan vaqtda javob bermadi',
        'Соединение с камерой отклонено': 'Kamera bilan ulanish rad etildi',
        'Для камеры не указаны данные доступа': 'Kamera uchun kirish ma’lumotlari ko‘rsatilmagan',
        'Сохранённые данные доступа повреждены': 'Saqlangan kirish ma’lumotlari buzilgan',
        'Проверки временно приостановлены после повторных сбоев': 'Takroriy nosozliklardan so‘ng tekshiruvlar vaqtincha to‘xtatildi',
        'Проверка завершилась с неизвестной ошибкой': 'Tekshiruv noma’lum xato bilan yakunlandi',
        'Проверка доступности сети': 'Tarmoq mavjudligini tekshirish',
        'Получение основных данных ONVIF': 'Asosiy ONVIF ma’lumotlarini olish',
        'Обработка ответа камеры': 'Kamera javobini qayta ishlash',
        'Сохранение сведений о прошивке': 'Proshivka ma’lumotlarini saqlash',
        'Сохранение данных камеры': 'Kamera ma’lumotlarini saqlash',
        'Обновление состояния камеры': 'Kamera holatini yangilash',
        'Обработка результатов диагностики': 'Diagnostika natijalarini qayta ishlash',
        'Сохранение результата проверки': 'Tekshiruv natijasini saqlash',
        'Не зафиксирован в старой записи': 'Eski yozuvda qayd etilmagan',
        'Точное время не сохранено': 'Aniq vaqt saqlanmagan',
        'Ошибка остаётся активной; успешная повторная проверка пока не зафиксирована.': 'Xato faol qolmoqda; muvaffaqiyatli qayta tekshiruv hali qayd etilmagan.',
        'Следующая проверка успешна': 'Keyingi tekshiruv muvaffaqiyatli',
        'Камера снова отвечает.': 'Kamera yana javob bermoqda.',
        'Здесь появятся только зафиксированные неисправности.': 'Bu yerda faqat qayd etilgan nosozliklar ko‘rsatiladi.',

        'Да': 'Ha',
        'Нет': 'Yo‘q',
        'Не менять': 'O‘zgartirmaslik',
        'Подробности': 'Tafsilotlar',
        'Комментарий': 'Izoh',
        'Метки': 'Belgilar',
        'Действие': 'Amal',
        'Цвет': 'Rang',
        'Время': 'Vaqt',
        'Дата': 'Sana',
        'Уровень': 'Daraja',
        'Подпись': 'Nomi',
        'Пока не заполнен': 'Hali to‘ldirilmagan',
        'Главный администратор': 'Bosh administrator',
        'Г': 'B',

        'Сводка состояния камер': 'Kameralar holati xulosasi',
        'Страницы камер': 'Kameralar sahifalari',
        'Подробности показателя': 'Ko‘rsatkich tafsilotlari',
        'Количество ошибок': 'Xatolar soni',
        'Например, Вход 1': 'Masalan, Kirish 1',
        'вход, этаж 1': 'kirish, 1-qavat',
        'Ответ ONVIF': 'ONVIF javobi',
        'Улица / адрес ·': 'Ko‘cha / manzil ·',
        'Риск < 52%': 'Xavf < 52%',
        'Норма по шкале: ниже 52%; от 52% — проверить, от 72% — высокий риск. Это индекс признаков, не вероятность поломки.': 'Shkala bo‘yicha me’yor: 52% dan past; 52% dan boshlab tekshirish, 72% dan boshlab yuqori xavf. Bu belgilar indeksi, nosozlik ehtimoli emas.',
        '35–220 из 255': '255 dan 35–220 gacha',
        'Ориентир оценки: 35–220 из 255. 0 — чёрный кадр, 255 — белый; ночной кадр может быть темнее.': 'Baholash mo‘ljali: 255 dan 35–220 gacha. 0 — qora kadr, 255 — oq; tungi kadr qorong‘iroq bo‘lishi mumkin.',
        'От 24': '24 dan boshlab',
        'Ориентир оценки: от 24. Разброс яркости пикселей; низкое значение означает почти однотонный кадр.': 'Baholash mo‘ljali: 24 dan boshlab. Piksel yorqinligi tarqalishi; past qiymat deyarli bir tusli kadrni anglatadi.',
        'От 8%': '8% dan boshlab',
        'Ориентир оценки: от 8%. Доля пикселей на границах предметов, а не процент исправности камеры.': 'Baholash mo‘ljali: 8% dan boshlab. Bu obyekt chegaralaridagi piksellar ulushi, kameraning sozlik foizi emas.',
        '5–8 бит': '5–8 bit',
        'Ориентир оценки: от 5 бит из 8. Чем выше, тем разнообразнее оттенки; шум тоже повышает значение.': 'Baholash mo‘ljali: 8 bitdan kamida 5 bit. Qiymat oshgani sari tuslar xilma-xil bo‘ladi; shovqin ham qiymatni oshiradi.',
        'Лучше ближе к 0 мс. Это время запроса до камеры и обратно; единая норма зависит от сети.': '0 ms ga yaqinroq bo‘lishi yaxshi. Bu kameraga so‘rov yuborish va javob qaytish vaqti; yagona me’yor tarmoqqa bog‘liq.',
        'Ориентир: ближе к 0 мс, задержка стабильнее. Разброс времени успешных ответов; нужны хотя бы 2 ответа из 3.': 'Mo‘ljal: 0 ms ga yaqin bo‘lsa, kechikish barqarorroq. Bu muvaffaqiyatli javoblar vaqtining tarqalishi; 3 tadan kamida 2 ta javob kerak.',
        'Норма: 0%. Доля неотвеченных запросов из 3; это не измерение потерь видеопотока. Камера может блокировать пинг.': 'Me’yor: 0%. Uchta so‘rov ichidagi javobsiz so‘rovlar ulushi; bu video oqimi yo‘qotilishini o‘lchamaydi. Kamera pingni bloklashi mumkin.',
        'Ожидается успешная загрузка. Меньше — быстрее; единой нормы в мс нет, время зависит от сети и размера кадра.': 'Muvaffaqiyatli yuklanish kutiladi. Kamroq — tezroq; ms bo‘yicha yagona me’yor yo‘q, vaqt tarmoq va kadr hajmiga bog‘liq.',
        'обновление': 'yangilanish',
        'ONVIF устройство': 'ONVIF qurilmasi',
        'Доступ к камере': 'Kameraga kirish',
        'Не настроен': 'Sozlanmagan',
        'Детали камеры': 'Kamera tafsilotlari',
        'Проверка ещё не выполнялась': 'Tekshiruv hali bajarilmagan',
        'Нет данных для расчёта': 'Hisoblash uchun ma’lumot yo‘q',
        'Нужен доступный кадр для анализа.': 'Tahlil uchun mavjud kadr kerak.',
        'Нет ответа или нет измерения': 'Javob yoki o‘lchov yo‘q',
        'Недостаточно ответов': 'Javoblar yetarli emas',
        'Нет измерения': 'O‘lchov yo‘q',
        'нет кадра': 'kadr yo‘q',
        'Камера участвует в автоматическом цикле. Нажмите, чтобы исключить.': 'Kamera avtomatik tekshiruv siklida. Chiqarish uchun bosing.',
        'Ошибок нет': 'Xatolar yo‘q',

        'Текущая проверка': 'Joriy tekshiruv',
        'Температура': 'Harorat',
        'Объектив': 'Obyektiv',
        'Скачивание кадра': 'Kadr yuklanmoqda',
        'Нажмите «Проверить сейчас» для получения кадра': 'Kadrni olish uchun «Hozir tekshirish» tugmasini bosing',
        'Сетевые настройки': 'Tarmoq sozlamalari',
        'Нажмите «Загрузить» для получения настроек...': 'Sozlamalarni olish uchun «Yuklash» tugmasini bosing...',
        'Загрузить сетевые настройки': 'Tarmoq sozlamalarini yuklash',
        'Нажмите «Загрузить» для получения списка пользователей...': 'Foydalanuvchilar ro‘yxatini olish uchun «Yuklash» tugmasini bosing...',
        'Загрузить пользователей': 'Foydalanuvchilarni yuklash',
        'Профили видео': 'Video profillari',
        'Нажмите «Загрузить» для получения профилей...': 'Profillarni olish uchun «Yuklash» tugmasini bosing...',
        'Загрузить профили': 'Profillarni yuklash',
        'Действия по обслуживанию': 'Xizmat ko‘rsatish amallari',
        'Нет сохранённых резервных копий.': 'Saqlangan zaxira nusxalari yo‘q.',
        'Проверка камеры ещё не запускалась.': 'Kamera tekshiruvi hali ishga tushirilmagan.',
        'СБОЙ': 'NOSOZLIK',
        'Не настроен доступ к камере': 'Kameraga kirish sozlanmagan',
        'Укажите логин и пароль ONVIF в настройках камеры.': 'Kamera sozlamalarida ONVIF login va parolini kiriting.',
        'Новая ошибка без ответственного': 'Mas’ul tayinlanmagan yangi xato',
        'Отметить просмотренным': 'Ko‘rilgan deb belgilash',
        'Ошибки камеры': 'Kamera xatolari',
        'Ошибки в заявке': 'Arizadagi xatolar',

        'Контроль активен': 'Nazorat faol',
        'В обработке': 'Qayta ishlanmoqda',
        'Обработка': 'Qayta ishlash',
        'Доступ: Администратор, Оператор, Техник': 'Ruxsat: Administrator, Operator, Texnik',
        'Доступ: Администратор, Техник': 'Ruxsat: Administrator, Texnik',
        'Доступ: Администратор, Оператор': 'Ruxsat: Administrator, Operator',
        'Результат проверен менеджером': 'Natija menejer tomonidan tekshirildi',
        'Сводка обработки ошибок': 'Xatolarni qayta ishlash xulosasi',
        'Предыдущие этапы': 'Oldingi bosqichlar',
        'Следующие этапы': 'Keyingi bosqichlar',
        'Закрытые менеджерами и автоматически устранённые ошибки появятся здесь.': 'Menejerlar yopgan va avtomatik bartaraf etilgan xatolar shu yerda paydo bo‘ladi.',

        'Изменения применяются сразу после сохранения.': 'O‘zgarishlar saqlangandan so‘ng darhol qo‘llanadi.',
        'Проверки': 'Tekshiruvlar',
        'обзор и просмотр камер': 'kameralarni ko‘rib chiqish',
        'работа с проверками и ошибками': 'tekshiruvlar va xatolar bilan ishlash',
        'настройка и обслуживание камер': 'kameralarni sozlash va ularga xizmat ko‘rsatish',
        'полный контроль и управление аккаунтами': 'to‘liq nazorat va akkauntlarni boshqarish',

        'Стабильность и периодичность': 'Barqarorlik va davriylik',
        'Яркость, нижняя граница из 255': 'Yorqinlik, 255 dan quyi chegara',
        'Яркость, верхняя граница из 255': 'Yorqinlik, 255 dan yuqori chegara',
        'Минимальный контраст': 'Minimal kontrast',
        'Минимальная энтропия из 8 бит': '8 bitdan minimal entropiya',
        'Типы событий и сроки напоминаний по карточкам': 'Hodisa turlari va kartochkalar bo‘yicha eslatma muddatlari',
        'Администратору — новые ошибки, технику — назначенные ему': 'Administratorga yangi xatolar, texnikka unga tayinlanganlari',
        'Уведомление менеджеру после завершения работы техником': 'Texnik ishni tugatgach menejerga bildirishnoma',
        'Карточка долго без изменений': 'Kartochka uzoq vaqt o‘zgarmadi',
        'Общие заметки календаря': 'Umumiy kalendar eslatmalari',
        'Показывать новое событие, минут': 'Yangi hodisani ko‘rsatish, daqiqa',
        'От 5 минут до 24 часов': '5 daqiqadan 24 soatgacha',
        'Считать карточку просроченной через, часов': 'Kartochkani muddati o‘tgan deb hisoblash, soat',
        'От 1 часа до 7 дней без изменений': '1 soatdan 7 kungacha o‘zgarishsiz',
        'Повторять напоминание каждые, часов': 'Eslatmani har necha soatda takrorlash',
        'Фактические данные по истории закрытых ошибок': 'Yopilgan xatolar tarixi bo‘yicha amaldagi ma’lumotlar',
        'Закрытых ошибок': 'Yopilgan xatolar',

        'Месяц': 'Oy',
        'Новых уведомлений нет': 'Yangi bildirishnomalar yo‘q',
        'Новых назначений и просроченных карточек нет': 'Yangi tayinlovlar va muddati o‘tgan kartochkalar yo‘q',
        'меньше минуты': 'bir daqiqadan kam',
        'Нужна настройка': 'Sozlash kerak',
        'Всегда доступно': 'Har doim mavjud'
    });

    const dynamicTranslations = [
        [/^Камера (\d+)$/u, match => `Kamera ${match[1]}`],
        [/^Камера (\d+) (.+)$/u, match => `Kamera ${match[1]} ${match[2]}`],
        [/^Камера (.+)$/u, match => `Kamera ${preserveUserText(match[1])}`],
        [/^Ошибка #(\d+)$/u, match => `Xato #${match[1]}`],
        [/^Ошибок (\d+)$/u, match => `Xatolar: ${match[1]}`],
        [/^(\d+) (?:ошибка|ошибки|ошибок)$/u, match => `${match[1]} ta xato`],
        [/^(\d+) из (\d+)$/u, match => `${match[1]} / ${match[2]}`],
        [/^Страница (\d+) из (\d+)$/u, match => `Sahifa ${match[1]} / ${match[2]}`],
        [/^Карточек: (\d+)$/u, match => `Kartochkalar: ${match[1]}`],
        [/^Заметок: (\d+)$/u, match => `Eslatmalar: ${match[1]}`],
        [/^Новых уведомлений: (\d+)$/u, match => `Yangi bildirishnomalar: ${match[1]}`],
        [/^(\d+) выбрано$/u, match => `${match[1]} ta tanlangan`],
        [/^(\d+) требуют внимания$/u, match => `E’tibor talab qiladi: ${match[1]}`],
        [/^Активных заметок в календаре: (\d+)$/u, match => `Kalendardagi faol eslatmalar: ${match[1]}`],
        [/^Все состояния \((\d+)\)$/u, match => `Barcha holatlar (${match[1]})`],
        [/^Только исключённые \((\d+)\)$/u, match => `Faqat chiqarilganlar (${match[1]})`],
        [/^Онлайн \((\d+)\)$/u, match => `Onlayn (${match[1]})`],
        [/^Офлайн \((\d+)\)$/u, match => `Oflayn (${match[1]})`],
        [/^Нет данных \((\d+)\)$/u, match => `Ma’lumot yo‘q (${match[1]})`],
        [/^Проверить \((\d+)\)$/u, match => `Tekshirish (${match[1]})`],
        [/^Выбрать (.+)$/u, match => `${preserveUserText(match[1])} ni tanlash`],
        [/^Открыть карточку (.+)$/u, match => `${preserveUserText(match[1])} kartochkasini ochish`],
        [/^Серийный номер карточки (.+)$/u, match => `Kartochka seriya raqami ${preserveUserText(match[1])}`],
        [/^Обновлено:? (.+)$/u, match => `Yangilandi: ${match[1]}`],
        [/^Ошибка: (.+)$/u, match => `Xato: ${match[1]}`],
        [/^Не настроен доступ к камере (.+)$/u, match => `Kameraga kirish sozlanmagan: ${preserveUserText(match[1])}`],
        [/^(.+): Не настроен доступ к камере (.+)$/u, match => `${preserveUserText(match[1])}: kameraga kirish sozlanmagan: ${preserveUserText(match[2])}`],
        [/^Техническая · (.+)$/u, match => `Texnik · ${match[1]}`],
        [/^Просрочено: (.+)$/u, match => `Muddati o‘tgan: ${match[1]}`],
        [/^До (.+)$/u, match => `${match[1]} gacha`],
        [/^(\d+) ошибок, (\d+) действий$/u, match => `${match[1]} ta xato, ${match[2]} ta amal`],
        [/^([\d.,]+) (ГБ|МБ) из ([\d.,]+) (ГБ|МБ)$/u, match => `${match[1]} ${match[2] === 'ГБ' ? 'GB' : 'MB'} / ${match[3]} ${match[4] === 'ГБ' ? 'GB' : 'MB'}`],
        [/^([\d.,]+) (ГБ|МБ) · GC ([\d.,]+) (ГБ|МБ)$/u, match => `${match[1]} ${match[2] === 'ГБ' ? 'GB' : 'MB'} · GC ${match[3]} ${match[4] === 'ГБ' ? 'GB' : 'MB'}`],
        [/^(\d+) физ\. (?:ядро|ядра|ядер) · (\d+) логич\.$/u, match => `${match[1]} ta jismoniy yadro · ${match[2]} ta mantiqiy`],
        [/^(\d+) д (\d+) ч$/u, match => `${match[1]} kun ${match[2]} soat`],
        [/^(\d+) ч (\d+) мин$/u, match => `${match[1]} soat ${match[2]} daq`],
        [/^(\d+) устройств$/u, match => `${match[1]} qurilma`],
        [/^(\d+) запис(?:ь|и|ей)$/u, match => `${match[1]} yozuv`],
        [/^(\d+) мин$/u, match => `${match[1]} daq`],
        [/^(\d+) ч$/u, match => `${match[1]} soat`],
        [/^(\d+) сек$/u, match => `${match[1]} soniya`],
        [/^(\d+(?:[.,]\d+)?) мс, (.+)$/u, match => `${match[1]} ms, ${match[2] === 'no_credentials' ? 'kirish ma’lumotlari sozlanmagan' : preserveUserText(match[2])}`],
        [/^(\d+(?:[.,]\d+)?) мс$/u, match => `${match[1]} ms`]
    ];

    const protectedLatinTokens = /\b(?:Camera Maintainer|OpenStreetMap|ONVIF|HTTP|HTTPS|CPU|RAM|NTP|PTZ|MAC|URL|IP|ID|FPS|JPEG|PNG|GB|MB|KB)\b/gu;

    function preserveUserText(value) {
        return `\uE100${value}\uE101`;
    }

    function latinToCyrillic(value) {
        const protectedValues = [];
        let text = value.replace(/\uE100([\s\S]*?)\uE101/gu, (_, token) => {
            const marker = `\uE000${protectedValues.length}\uE001`;
            protectedValues.push(token);
            return marker;
        });
        text = text.replace(protectedLatinTokens, token => {
            const marker = `\uE000${protectedValues.length}\uE001`;
            protectedValues.push(token);
            return marker;
        });

        const sequences = [
            ['O‘', 'Ў'], ['O’', 'Ў'], ["O'", 'Ў'], ['o‘', 'ў'], ['o’', 'ў'], ["o'", 'ў'],
            ['G‘', 'Ғ'], ['G’', 'Ғ'], ["G'", 'Ғ'], ['g‘', 'ғ'], ['g’', 'ғ'], ["g'", 'ғ'],
            ['Sh', 'Ш'], ['SH', 'Ш'], ['sh', 'ш'], ['Ch', 'Ч'], ['CH', 'Ч'], ['ch', 'ч'],
            ['Yo', 'Ё'], ['YO', 'Ё'], ['yo', 'ё'], ['Yu', 'Ю'], ['YU', 'Ю'], ['yu', 'ю'],
            ['Ya', 'Я'], ['YA', 'Я'], ['ya', 'я'], ['Ts', 'Ц'], ['TS', 'Ц'], ['ts', 'ц']
        ];
        sequences.forEach(([latin, cyrillic]) => { text = text.split(latin).join(cyrillic); });
        text = text.replace(/\bE/g, 'Э').replace(/\be/g, 'э');

        const characters = {
            A: 'А', a: 'а', B: 'Б', b: 'б', D: 'Д', d: 'д', E: 'Е', e: 'е',
            F: 'Ф', f: 'ф', G: 'Г', g: 'г', H: 'Ҳ', h: 'ҳ', I: 'И', i: 'и',
            J: 'Ж', j: 'ж', K: 'К', k: 'к', L: 'Л', l: 'л', M: 'М', m: 'м',
            N: 'Н', n: 'н', O: 'О', o: 'о', P: 'П', p: 'п', Q: 'Қ', q: 'қ',
            R: 'Р', r: 'р', S: 'С', s: 'с', T: 'Т', t: 'т', U: 'У', u: 'у',
            V: 'В', v: 'в', X: 'Х', x: 'х', Y: 'Й', y: 'й', Z: 'З', z: 'з',
            '‘': 'ъ', '’': 'ъ', "'": 'ъ'
        };
        text = Array.from(text, character => characters[character] || character).join('');
        return text.replace(/\uE000(\d+)\uE001/g, (_, index) => protectedValues[Number(index)]);
    }

    function requestedLanguage() {
        try {
            const queryLanguage = new URLSearchParams(window.location.search).get('lang');
            if (supportedLanguages.has(queryLanguage)) return queryLanguage;
            const savedLanguage = localStorage.getItem(storageKey);
            if (supportedLanguages.has(savedLanguage)) return savedLanguage;
        } catch (_) { }
        return 'ru';
    }

    const currentLanguage = requestedLanguage();
    const intlLocales = { ru: 'ru-RU', 'uz-Latn': 'uz-Latn-UZ', 'uz-Cyrl': 'uz-Cyrl-UZ' };

    function normalized(value) {
        return String(value ?? '').trim().replace(/\s+/g, ' ');
    }

    function translateCore(source) {
        if (Object.prototype.hasOwnProperty.call(uzLatin, source)) return uzLatin[source];
        for (const [pattern, translateMatch] of dynamicTranslations) {
            const match = source.match(pattern);
            if (match) return translateMatch(match);
        }
        const titleSuffix = ' | Центр мониторинга камер';
        if (source.endsWith(titleSuffix)) {
            const pageTitle = source.slice(0, -titleSuffix.length);
            return `${translateCore(pageTitle) || pageTitle} | Kameralarni monitoring qilish markazi`;
        }
        return null;
    }

    function translate(value) {
        if (currentLanguage === 'ru' || typeof value !== 'string') return value;
        const source = normalized(value);
        if (!source) return value;
        const translated = translateCore(source);
        if (!translated) return value;
        const result = currentLanguage === 'uz-Cyrl'
            ? latinToCyrillic(translated)
            : translated.replace(/[\uE100\uE101]/gu, '');
        const leading = value.match(/^\s*/u)?.[0] || '';
        const trailing = value.match(/\s*$/u)?.[0] || '';
        return leading + result + trailing;
    }

    function shouldSkip(node) {
        const parent = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
        return !parent || Boolean(parent.closest('script, style, code, pre, textarea, [data-i18n-skip]'));
    }

    const translatedAttributes = ['title', 'aria-label', 'placeholder'];

    function translateAttributes(element) {
        if (!(element instanceof Element) || shouldSkip(element)) return;
        translatedAttributes.forEach(attribute => {
            if (!element.hasAttribute(attribute)) return;
            const value = element.getAttribute(attribute);
            const result = translate(value);
            if (result !== value) element.setAttribute(attribute, result);
        });
    }

    function translateElement(element) {
        if (!(element instanceof Element) || shouldSkip(element)) return;
        translateAttributes(element);
        element.querySelectorAll('[title], [aria-label], [placeholder]').forEach(translateAttributes);
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let textNode = walker.nextNode();
        while (textNode) {
            if (!shouldSkip(textNode)) {
                const result = translate(textNode.nodeValue);
                if (result !== textNode.nodeValue) textNode.nodeValue = result;
            }
            textNode = walker.nextNode();
        }
    }

    function translateDocument() {
        document.documentElement.lang = currentLanguage;
        document.documentElement.dataset.language = currentLanguage;
        const translatedTitle = translate(document.title);
        if (translatedTitle !== document.title) document.title = translatedTitle;
        if (document.body) translateElement(document.body);
        document.querySelectorAll('[data-language-selector]').forEach(selector => {
            selector.value = currentLanguage;
        });
    }

    function observeTranslations() {
        if (!document.body || currentLanguage === 'ru') return;
        const observer = new MutationObserver(mutations => {
            mutations.forEach(mutation => {
                if (mutation.type === 'characterData') {
                    if (!shouldSkip(mutation.target)) {
                        const result = translate(mutation.target.nodeValue);
                        if (result !== mutation.target.nodeValue) mutation.target.nodeValue = result;
                    }
                    return;
                }
                if (mutation.type === 'attributes') {
                    translateAttributes(mutation.target);
                    return;
                }
                mutation.addedNodes.forEach(node => {
                    if (node.nodeType === Node.TEXT_NODE && !shouldSkip(node)) {
                        const result = translate(node.nodeValue);
                        if (result !== node.nodeValue) node.nodeValue = result;
                    } else if (node.nodeType === Node.ELEMENT_NODE) {
                        translateElement(node);
                    }
                });
            });
        });
        observer.observe(document.body, {
            subtree: true,
            childList: true,
            characterData: true,
            attributes: true,
            attributeFilter: translatedAttributes
        });
    }

    function setLanguage(language) {
        if (!supportedLanguages.has(language)) return;
        try { localStorage.setItem(storageKey, language); } catch (_) { }
        window.location.reload();
    }

    window.uiLocale = Object.freeze({
        language: currentLanguage,
        intlLocale: intlLocales[currentLanguage],
        labels: languageLabels,
        setLanguage,
        t: translate,
        toCyrillic: latinToCyrillic
    });
    document.documentElement.lang = currentLanguage;
    document.documentElement.dataset.language = currentLanguage;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            translateDocument();
            observeTranslations();
        }, { once: true });
    } else {
        translateDocument();
        observeTranslations();
    }
})();
