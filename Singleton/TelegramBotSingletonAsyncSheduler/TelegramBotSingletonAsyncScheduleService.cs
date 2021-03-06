﻿////////////////////////////////////////////////
// © https://github.com/badhitman - @fakegov
////////////////////////////////////////////////
using AbstractAsyncScheduler;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramBot.TelegramMetadata;
using TelegramBot.TelegramMetadata.AvailableTypes;
using TelegramBot.TelegramMetadata.GettingUpdates;
using static TelegramBot.TelegramMetadata.AvailableTypes.MessageClass;

namespace TelegramBotSingletonAsyncSheduler
{
    public class TelegramBotSingletonAsyncScheduleService : BasicSingletonScheduler
    {
        public enum ChatTypes { Private, Group, Supergroup, Channel, Error }

        public string TelegramBotApiKey { get; private set; }
        public IMemoryCache cache;
        public TelegramClientCore TelegramClient { get; private set; }
        public bool TelegramClientNotLoaded => TelegramClient?.Me is null || TelegramClient.Me.id == 0;


        /// <summary>
        /// Транзитный набор полученных обновлений от TelegramBot 
        /// </summary>
        public ConcurrentBag<Update> TelegramBotUpdates { get; set; } = new ConcurrentBag<Update>();

        public TelegramBotSingletonAsyncScheduleService(ILoggerFactory set_logger_factory, IMemoryCache memoryCache, string set_telegram_bot_api_key, int set_schedule_pause_period)
            : base(set_logger_factory, set_schedule_pause_period)
        {
            cache = memoryCache;
            TelegramBotApiKey = set_telegram_bot_api_key;
            AuthTelegramBotAsync();
        }


        #region async senders

        /// <summary>
        /// [async] отправить текстовое сообщение TelegramBot
        /// </summary>
        public async void SendMessageTelegramBotAsync(long chat_id, string message_text, ParseModes parse_mode = ParseModes.Markdown)
        {
            SetStatus("Отправка сообщения в чат: " + chat_id);
            await Task.Run(() =>
            {
                try
                {
                    MessageClass message = TelegramClient.sendMessage(chat_id.ToString(), message_text, parse_mode);
                    SetStatus("Сообщение отправлено");
                }
                catch
                {
                    SetStatus("Ошибка отправки сообщения.", StatusTypes.ErrorStatus);
                    SetStatus(TelegramClient.HttpRrequestStatus);
                }
                SetStatus(null);
            });
        }

        /// <summary>
        /// [async] отправить фото TelegramBot
        /// </summary>
        public async void SendPhotoTelegramBotAsync(long chat_id, InputFileClass photo, string caption = null, string telegram_cloud_cache_id = null)
        {
            SetStatus("Отправка фото");

            await Task.Run(() =>
            {
                MessageClass message;
                if (string.IsNullOrWhiteSpace(telegram_cloud_cache_id))
                {
                    message = TelegramClient.sendPhoto(chat_id.ToString(), photo, caption, ParseModes.Markdown);
                }
                else
                {
                    string uploaded_photo_cache = null;
                    if (cache.TryGetValue(telegram_cloud_cache_id, out uploaded_photo_cache))
                    {
                        message = TelegramClient.sendPhoto(chat_id.ToString(), uploaded_photo_cache, caption, ParseModes.Markdown);
                    }
                    else
                    {
                        message = TelegramClient.sendPhoto(chat_id.ToString(), photo, caption, ParseModes.Markdown);
                        cache.Set(telegram_cloud_cache_id, message.photo[0].file_id, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(48)));
                    }
                }
                SetStatus(null);
            });
        }

        public async void SendMediaGroupTelegramBotAsync(long chat_id, InputFileClass[] files)
        {
            SetStatus("Отправка группы файлов");
            await Task.Run(() =>
            {
                foreach (InputFileClass file in files)
                {
                    MessageClass message = TelegramClient.sendDocument(chat_id.ToString(), file);
                    Thread.Sleep(500);
                }
            });
        }

        /// <summary>
        /// [async] отправить документ TelegramBot
        /// </summary>
        public async void SendDocumentTelegramBotAsync(long chat_id, InputFileClass document, string caption = null)
        {
            SetStatus("Отправка документа");
            await Task.Run(() =>
            {
                MessageClass message = TelegramClient.sendDocument(chat_id.ToString(), document, caption);
            });
        }

        #endregion

        public async void AuthTelegramBotAsync()
        {
            SetStatus("Попытка авторизации TelegramBot: " + GetType().Name);
            if (string.IsNullOrWhiteSpace(TelegramBotApiKey))
            {
                SetStatus("Ошибка авторизации TelegramBot. Не установлен TelegramBotApiKey", StatusTypes.ErrorStatus);
                SetStatus(null);
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    TelegramClient = new TelegramClientCore(TelegramBotApiKey);

                    if (EnableFullRawTracert)
                        SetStatus("http_response_raw" + TelegramClient.http_response_raw, StatusTypes.DebugStatus);

                    if (TelegramClientNotLoaded)
                    {
                        SetStatus("Ошибка проверки TelegramBot: <TelegramClientNotLoaded>", StatusTypes.ErrorStatus);
                        SetStatus(null);
                        return;
                    }
                    SetStatus("TelegramBot авторизован:" + TelegramClient.Me.ToString());
                }
                catch (Exception e)
                {
                    SetStatus("Ошибка загрузки бота: " + e.Message, StatusTypes.ErrorStatus);
                    SetStatus("HttpRrequestStatus: " + TelegramClient.HttpRrequestStatus, StatusTypes.ErrorStatus);
                    SetStatus(null);
                    return;
                }
                Thread.Sleep(500);
                SetStatus("Запрос обновлений TelegramBot");
                ScheduleBodyAsyncAction();
            });
        }

        protected override void ScheduleBodyAsyncAction()
        {
            if (TelegramClientNotLoaded)
            {
                SetStatus("Ошибка проверки TelegramBot: <TelegramClientNotLoaded>", StatusTypes.ErrorStatus);
                if (string.IsNullOrWhiteSpace(ScheduleStatus))
                {
                    SetStatus("Повторная попытка авторизации и загрузки TelegramBot");
                    AuthTelegramBotAsync();
                }
                else
                    SetStatus(null);

                return;
            }

            Update[] Updates;

            try
            {
                SetStatus("Запрос обновлений с сервера TelegramBot");
                Updates = TelegramClient.getUpdates();

                if (EnableFullRawTracert)
                    SetStatus("http_response_raw" + TelegramClient.http_response_raw, StatusTypes.DebugStatus);
            }
            catch
            {
                SetStatus("Ошибка при попытке получить обновления");
                SetStatus(TelegramClient.HttpRrequestStatus);
                SetStatus(null, StatusTypes.DebugStatus);
                return;
            }
            if (Updates == null)
            {
                SetStatus("Обновления TelegramBot - IS NULL");
                SetStatus(TelegramClient.HttpRrequestStatus);
                SetStatus(null, StatusTypes.DebugStatus);
                return;
            }

            if (Updates.Length > 0)
            {
                lock (TelegramBotUpdates)
                {
                    TelegramClient.offset = Updates.Max(x => x.update_id);
                    SetStatus("Размещение TelegramBot обновлений во временное хранилище. [" + Updates.Length + "] шт");
                    ChatTypes ChatType;
                    string chat_type_as_string;
                    foreach (Update u in Updates)
                    {
                        #region Определение типа чата
                        ChatType = ChatTypes.Error;
                        chat_type_as_string = u.message.chat.type.ToLower();
                        foreach (ChatTypes c in Enum.GetValues(typeof(ChatTypes)))
                        {
                            if (c.ToString("g").ToLower() == chat_type_as_string)
                            {
                                ChatType = c;
                                break;
                            }
                        }
                        #endregion

                        if (ChatType == ChatTypes.Private && !TelegramBotUpdates.Any(x => x.update_id == u.update_id))
                            TelegramBotUpdates.Add(u);
                    }
                    try
                    {
                        Thread.Sleep(500);
                        SetStatus("Сдвиг/Закрытие очереди получения обновлений от TelegramBot");
                        TelegramClient.getUpdates(1);
                        if (EnableFullRawTracert)
                            SetStatus("http_response_raw" + TelegramClient.http_response_raw, StatusTypes.DebugStatus);
                    }
                    catch (Exception e)
                    {
                        SetStatus("Ошибка  TelegramClient.getUpdates(1) " + e.Message, StatusTypes.ErrorStatus);
                        SetStatus(null);
                    }
                }
            }
            else
                SetStatus("Без TelegramBot обновлений", StatusTypes.DebugStatus);

            SetStatus(null);
        }

        public override void InvokeSchedule()
        {
            if (TelegramClientNotLoaded)
            {
                SetStatus("Ошибка проверки TelegramBot: <TelegramClientNotLoaded>", StatusTypes.ErrorStatus);
                if (string.IsNullOrWhiteSpace(ScheduleStatus))
                {
                    SetStatus("Повторная попытка авторизации и загрузки TelegramBot");
                    AuthTelegramBotAsync();
                }
                else
                    SetStatus(null);

                return;
            }
            base.InvokeSchedule();
        }
    }
}
