using Npgsql;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using File = System.IO.File;

public class BotHandler
{
    public string Token { get; set; }
    public object currentTime = DateTime.Now.ToString("HH:mm");

    public readonly long[] Admins = { 5091219046, 5349408431, 6285344448 };
    
    private readonly Dictionary<long, List<string>> userPagination = new Dictionary<long, List<string>>();
    private readonly string connectionString = "Host=localhost;Port=5432;Username=postgres;Password=2712;Database=Rumassa;";
    private readonly Dictionary<long, string> userStates = new Dictionary<long, string>();

    public BotHandler(string token)
    {
        Token = token;
    }

    public async Task BotHandle()
    {
        try
        {
            var botClient = new TelegramBotClient(Token);

            using var cts = new CancellationTokenSource();

            ReceiverOptions receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() 
            };

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();
            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine();

            cts.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

    private async Task<HashSet<long>> GetUniqueUserIdsAsync()
    {
        var userIds = new HashSet<long>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand("SELECT DISTINCT ChatId FROM AllMessages", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            userIds.Add(reader.GetInt64(0));
        }

        return userIds;
    }

    public async Task SendAnnouncementToAllUsersAsync(string announcementMessage)
    {
        var botClient = new TelegramBotClient(Token);
        var userIds = await GetUniqueUserIdsAsync();

        foreach (var userId in userIds)
        {
            try
            {
                await botClient.SendTextMessageAsync(
                    chatId: userId,
                    text: announcementMessage
                );
                Console.WriteLine($"Message sent to user ID: {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send message to user ID: {userId}, Error: {ex.Message}");
            }
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var messageText = update.Message?.Text;
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? 0;

        if (chatId == 0)
        {
            Console.WriteLine("Chat not found. User may have blocked the bot.");
            return;
        }

        if (update.CallbackQuery != null)
        {
            await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken);
            return;
        }

        if (messageText == null && update.Message?.Type != MessageType.Contact) return;

        try
        {
            await botClient.GetChatAsync(chatId, cancellationToken);
        }
        catch (ApiRequestException ex) when (ex.ErrorCode == 400)
        {
            Console.WriteLine("Invalid chat ID.");
            return;
        }

        if (update.Message?.Type == MessageType.Contact)
        {
            var contact = update.Message.Contact;
            var phoneNumber = contact.PhoneNumber;
            var firstName = contact.FirstName;
            var lastName = contact.LastName;
            var userId = contact.UserId;

            var user = $"Received contact: {firstName} {lastName}, Phone: {phoneNumber}, User ID: {userId}\n";
            string filepath1 = "/Users/otabek_coding/TEDxYouthYangikorgan/Registered_users.txt";

            File.AppendAllText(filepath1, user);

            await SendInlineButtonsAsync(update.Message.Chat.Id, botClient, cancellationToken);
            await SaveUserToDatabase(firstName, phoneNumber, userId);

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Endi bizga qo'shimcha ma'lumot sifatida bularni jo'nating:\n\n1. Ism & Familiyangiz (e.g Cristiano Ronaldu);\n2. Yashash manzilingiz(e.g Farg'ona viloyati, Toshloq tumani)\n3. Yoshingiz(e.g 19)\n4. O'zingiz haqingizda qisqacha\n\n\n📌Keyin siz bilan o'zimiz aloqaga chiqamiz!",
                cancellationToken: cancellationToken);

            foreach (var adminId in Admins)
            {
                await botClient.SendTextMessageAsync(
                    chatId: adminId,
                    text: user,
                    cancellationToken: cancellationToken);
            }
        }
        else if (update.Message?.Type == MessageType.Text)
        {
            var user = update.Message.Chat.FirstName;
            var chatBio = update.Message.Chat.Username;
            string user_message = $"Message:\n {messageText}\nchat: {chatId}\nBio: @{chatBio}\nfrom {user}\nat {currentTime}\n\n";

            string filepath = "/Users/otabek_coding/TEDxYouthYangikorgan/All_Messages.txt";

            File.AppendAllText(filepath, user_message);

            await SaveMessageToDatabase(user, chatBio, messageText, chatId);

            if (userStates.ContainsKey(chatId) && userStates[chatId] == "awaiting_announcement")
            {
                await SendAnnouncementToAllUsersAsync(messageText);
                userStates.Remove(chatId);
            }
            else if (userStates.ContainsKey(chatId) && userStates[chatId] == "awaiting_username")
            {
                await SearchUserAsync(botClient, chatId, messageText, cancellationToken);
                userStates.Remove(chatId);
            }
            else if (messageText == "/start" || messageText.ToLower().Equals("start"))
            {
                await SendStartMessageAsync(chatId, botClient, cancellationToken);
            }
            else if (messageText == "/register" || messageText.ToLower().Equals("register"))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Register tugmasini bosing!",
                    cancellationToken: cancellationToken);
            }
            else if (messageText == "/announce")
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Bu command faqat adminlar uchun!",
                    cancellationToken: cancellationToken);
            }
            else if (messageText == "/admin" || messageText.ToLower().Equals("admin") || messageText.ToLower().Equals("login"))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Please enter your login password for admin panel",
                    cancellationToken: cancellationToken);

                userStates[chatId] = "awaiting_admin_password";
            }
            else if (userStates.ContainsKey(chatId) && userStates[chatId] == "awaiting_admin_password")
            {
                if ((messageText == "oTabeK2007!" || messageText == "SarDorBek$2006") && Admins.Contains(chatId))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Welcome to the admin panel!",
                        cancellationToken: cancellationToken);

                    InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Search a user", "search_user"),
                        InlineKeyboardButton.WithCallbackData("Make announcement", "make_announcement"),
                        InlineKeyboardButton.WithCallbackData("All users", "all_users") // New button for "All users"
                    });

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Choose an action:",
                        replyMarkup: inlineKeyboard,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Incorrect Password or You are not admin!",
                        cancellationToken: cancellationToken);
                }
                userStates.Remove(chatId);
            }

            foreach (var adminId in Admins)
            {
                await botClient.SendTextMessageAsync(
                    chatId: adminId,
                    text: user_message,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;

        if (callbackQuery.Data == "search_user")
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Please enter the username without '@':",
                cancellationToken: cancellationToken);
        
            userStates[chatId] = "awaiting_username";
        }
        else if (callbackQuery.Data == "make_announcement")
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Please enter the announcement text:",
                cancellationToken: cancellationToken);
        
            userStates[chatId] = "awaiting_announcement";
        }
        else if (callbackQuery.Data == "all_users")
        {
            await GetAllUsersAsync(botClient, chatId, 0, cancellationToken);
        }
        else if (callbackQuery.Data.StartsWith("prev_"))
        {
            var page = int.Parse(callbackQuery.Data.Split('_')[1]);
            await SendPaginatedUsersAsync(botClient, chatId, page, cancellationToken);
        }
        else if (callbackQuery.Data.StartsWith("next_"))
        {
            var page = int.Parse(callbackQuery.Data.Split('_')[1]);
            await SendPaginatedUsersAsync(botClient, chatId, page, cancellationToken);
        }
    }

    private async Task GetAllUsersAsync(ITelegramBotClient botClient, long chatId, int page, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand(
            @"SELECT DISTINCT
                ru.FirstName,
                ru.PhoneNumber,
                ru.UserId,
                am.ChatBio
              FROM
                Registered_Users ru
              LEFT JOIN
                AllMessages am ON ru.UserId = am.ChatId", conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        var usersInfo = new List<string>();

        while (await reader.ReadAsync())
        {
            var firstName = reader.GetString(0);
            var phoneNumber = reader.GetString(1);
            var userId = reader.GetInt64(2);
            var chatBio = reader.IsDBNull(3) ? "N/A" : reader.GetString(3);

            var userInfo = $"Name: {firstName}\nPhone: {phoneNumber}\nUser ID: {userId}\nBio: @{chatBio}";
            usersInfo.Add(userInfo);
        }

        userPagination[chatId] = usersInfo;

        await SendPaginatedUsersAsync(botClient, chatId, page, cancellationToken);
    }

    private async Task SendPaginatedUsersAsync(ITelegramBotClient botClient, long chatId, int page, CancellationToken cancellationToken)
    {
        if (!userPagination.ContainsKey(chatId)) return;

        var usersInfo = userPagination[chatId];
        var pageSize = 50;
        var totalUsers = usersInfo.Count;
        var totalPages = (totalUsers + pageSize - 1) / pageSize;

        var startIndex = page * pageSize;
        var endIndex = Math.Min(startIndex + pageSize, totalUsers);

        if (startIndex >= totalUsers)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "No more users.",
                cancellationToken: cancellationToken);
            return;
        }

        var paginatedUsers = usersInfo.GetRange(startIndex, endIndex - startIndex);
        var usersText = string.Join("\n\n", paginatedUsers);

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Registered Users (Page {page + 1}):\n\n{usersText}",
            cancellationToken: cancellationToken);

        var inlineKeyboard = new List<List<InlineKeyboardButton>>();

        if (page > 0)
        {
            inlineKeyboard.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("Previous", $"prev_{page - 1}") });
        }

        if (endIndex < totalUsers)
        {
            inlineKeyboard.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("Next", $"next_{page + 1}") });
        }

        inlineKeyboard.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("Back", "/start") });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Choose an action:",
            replyMarkup: new InlineKeyboardMarkup(inlineKeyboard),
            cancellationToken: cancellationToken);
    }

    private async Task SearchUserAsync(ITelegramBotClient botClient, long chatId, string username, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand("SELECT UserName, ChatBio, MessageText, ChatId, Time FROM AllMessages WHERE ChatBio = @username", conn);
        cmd.Parameters.AddWithValue("username", username);

        await using var reader = await cmd.ExecuteReaderAsync();
        bool userFound = false;

        while (await reader.ReadAsync())
        {
            var user = reader.GetString(0);
            var chatBio = reader.GetString(1);
            var messageText = reader.GetString(2);
            var fetchedChatId = reader.GetInt64(3);
            var time = reader.GetDateTime(4);

            var userInfo = $"🤥 User: {user}\n🔗 Username: @{chatBio}\n✍️ Message: {messageText}\n🤫 Chat ID: {fetchedChatId}\n🧭 Time: {time}";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: userInfo,
                cancellationToken: cancellationToken);

            userFound = true;
        }

        if (!userFound)
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "User not found",
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendStartMessageAsync(long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        ReplyKeyboardMarkup replyKeyboardMarkup = new(new[]
        {
            new KeyboardButton[] { KeyboardButton.WithRequestContact("Register"), ("Login"),  }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "👋 *Assalomu Aleykum* \n\n *Bu bot orqali Fursat uchun ro'yhatdan o'tishingiz mumkin \n\n Register tugmasini bosing*",
            parseMode: ParseMode.MarkdownV2,
            replyMarkup: replyKeyboardMarkup,
            cancellationToken: cancellationToken);
    }

    private async Task SaveMessageToDatabase(string user, string chatBio, string messageText, long chatId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand(
            "INSERT INTO AllMessages(UserName, ChatBio, MessageText, ChatId, Time) VALUES (@userName, @chatBio, @messageText, @chatId, @time)",
            conn);
        cmd.Parameters.AddWithValue("userName", user ?? string.Empty);
        cmd.Parameters.AddWithValue("chatBio", chatBio ?? string.Empty);
        cmd.Parameters.AddWithValue("messageText", messageText);
        cmd.Parameters.AddWithValue("chatId", chatId);
        cmd.Parameters.AddWithValue("time", DateTime.Now);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SaveUserToDatabase(string firstName, string phoneNumber, long? userId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var cmd = new NpgsqlCommand("INSERT INTO Registered_Users (FirstName, PhoneNumber, UserId) VALUES (@firstName, @phoneNumber, @userId)", conn);
        cmd.Parameters.AddWithValue("firstName", firstName);
        cmd.Parameters.AddWithValue("phoneNumber", phoneNumber);
        cmd.Parameters.AddWithValue("userId", userId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PTexts(long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "*Noto'g'ri command*",
            parseMode: ParseMode.MarkdownV2,
            cancellationToken: cancellationToken
        );
    }

    private async Task SendInlineButtonsAsync(long chatId, ITelegramBotClient botClient, CancellationToken cancellationToken)
    {
        InlineKeyboardMarkup inlineKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("Telegram", "https://t.me/Otabek_Writing"),
                InlineKeyboardButton.WithUrl("Youtube", "https://www.youtube.com/@otabekmeliqoziyev5530")
            },
            new[]
            {
                InlineKeyboardButton.WithUrl("Instagram", "https://www.instagram.com/m_otabek_007/"),
                InlineKeyboardButton.WithUrl("Our Partner", "https://t.me/farrukhjonIELTS"),
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "*Ro'yhatdan muvaffaqqiyatli o'tdingiz* \n\nBizni ijtomiy tarmoqlarda kuzating",
            parseMode: ParseMode.MarkdownV2,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken
        );
    }

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine($"Error in polling: {errorMessage}");

            if (exception is ApiRequestException apiEx && apiEx.ErrorCode == 403)
            {
                Console.WriteLine("User has blocked the bot.");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error handling polling error: {e}");
        }

        return Task.CompletedTask;
    }
}