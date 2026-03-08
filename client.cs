using System.Net.Http.Json;
using System.Text.Json;

namespace MessengerClient;

public class Program
{
    private static readonly HttpClient client = new();
    private static string? serverUrl = "http://127.0.0.1:5267";
    private static string? jwtToken;
    private static DateTime tokenExpires;
    private static string? currentUserEmail;
    private static string? currentUserId;
    private static string? refreshToken;
    private static string? currentSessionId;

    public static async Task Main(string[] args)
    {
        Console.Title = "Messenger JWT Client";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Messenger JWT Authentication Client ===\n");
        Console.ResetColor();

        while (true)
        {
            ShowMenu();
            var choice = Console.ReadKey(true).KeyChar;

            Console.Clear();
            try
            {
                switch (choice)
                {
                    case '1':
                        await RegisterAsync();
                        break;
                    case '2':
                        await LoginAsync();
                        break;
                    case '3':
                        await TestProtectedEndpointAsync();
                        break;
                    case '6':
                        await ShowSessionsAsync();
                        break;
                    case '7':
                        await RevokeSessionInteractiveAsync();
                        break;
                    case '8':
                        await LogoutAsync();
                        break;
                    case '4':
                        await TestPublicEndpointAsync();
                        break;
                    case '5':
                        ShowTokenInfo();
                        break;
                    case '0':
                        Console.WriteLine("Выход из программы...");
                        return;
                    default:
                        Console.WriteLine("Неверный выбор. Попробуйте снова.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОшибка: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nНажмите любую клавишу для продолжения...");
            Console.ReadKey(true);
            Console.Clear();
        }
    }

    private static void ShowMenu()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("=== Меню ===");
        Console.ResetColor();

        if (string.IsNullOrEmpty(jwtToken))
        {
            Console.WriteLine("1. Регистрация");
            Console.WriteLine("2. Вход");
        }
        else
        {
            Console.WriteLine($"[Авторизован как: {currentUserEmail} (ID: {currentUserId})]");
            Console.WriteLine("3. Тест защищенного эндпоинта");
            Console.WriteLine("6. Показать сессии");
            Console.WriteLine("7. Отозвать сессию");
            Console.WriteLine("8. Выйти (revoke текущую сессию)");
        }

        Console.WriteLine("4. Тест публичного эндпоинта");
        Console.WriteLine("5. Показать информацию о токене");
        Console.WriteLine("0. Выход");
        Console.Write("\nВыберите действие: ");
    }

    private static async Task ShowSessionsAsync()
    {
        if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(currentUserId))
        {
            Console.WriteLine("Необходимо войти, чтобы просмотреть сессии.");
            return;
        }

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);

        try
        {
            var response = await client.GetAsync($"{serverUrl}/api/auth/sessions/{currentUserId}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Ошибка получения сессий: {response.StatusCode}");
                return;
            }

            var sessions = await response.Content.ReadFromJsonAsync<JsonElement[]>();
            Console.WriteLine($"Найдено сессий: {sessions?.Length ?? 0}");
            if (sessions == null || sessions.Length == 0) return;

            for (int i = 0; i < sessions.Length; i++)
            {
                var s = sessions[i];
                var id = s.GetProperty("id").GetString();
                var dev = s.TryGetProperty("deviceInfo", out var d) ? d.GetString() : string.Empty;
                var ip = s.TryGetProperty("ip", out var ipr) ? ipr.GetString() : string.Empty;
                var expires = s.TryGetProperty("expiresAt", out var ex) ? ex.GetDateTime() : DateTime.MinValue;
                var revoked = s.TryGetProperty("isRevoked", out var rv) ? rv.GetBoolean() : false;
                var created = s.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.MinValue;
                Console.WriteLine($"[{i}] Id: {id}\n    Device: {dev}\n    IP: {ip}\n    Created: {created}\n    Expires: {expires}\n    Revoked: {revoked}\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static async Task RevokeSessionInteractiveAsync()
    {
        await ShowSessionsAsync();
        Console.Write("Введите индекс сессии для отзыва (или пусто чтобы отменить): ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) return;
        if (!int.TryParse(input, out var idx))
        {
            Console.WriteLine("Неверный индекс");
            return;
        }

        // get sessions again to pick id
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
        try
        {
            var response = await client.GetAsync($"{serverUrl}/api/auth/sessions/{currentUserId}");
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Ошибка получения сессий: {response.StatusCode}");
                return;
            }

            var sessions = await response.Content.ReadFromJsonAsync<JsonElement[]>();
            if (sessions == null || idx < 0 || idx >= sessions.Length)
            {
                Console.WriteLine("Индекс вне диапазона");
                return;
            }

            var sessionId = sessions[idx].GetProperty("id").GetString();
            if (string.IsNullOrEmpty(sessionId))
            {
                Console.WriteLine("Не удалось получить id сессии");
                return;
            }

            var payload = new
            {
                sessionId = Guid.Parse(sessionId),
                userId = Guid.Parse(currentUserId!)
            };

            var revokeResp = await client.PostAsJsonAsync($"{serverUrl}/api/auth/sessions/revoke", payload);
            if (revokeResp.IsSuccessStatusCode)
            {
                Console.WriteLine("Сессия успешно отозвана.");
                // if revoked current session, clear local auth
                if (currentSessionId == sessionId)
                {
                    ClearLocalAuth();
                }
            }
            else
            {
                var err = await revokeResp.Content.ReadAsStringAsync();
                Console.WriteLine($"Ошибка отзыва сессии: {revokeResp.StatusCode} - {err}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static async Task LogoutAsync()
    {
        if (string.IsNullOrEmpty(jwtToken))
        {
            Console.WriteLine("Вы не авторизованы.");
            return;
        }

        if (string.IsNullOrEmpty(currentSessionId))
        {
            // just clear local
            ClearLocalAuth();
            Console.WriteLine("Локальный выход выполнен (сессия не найдена на клиенте).");
            return;
        }

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);
        try
        {
            var payload = new
            {
                sessionId = Guid.Parse(currentSessionId),
                userId = Guid.Parse(currentUserId!)
            };

            var revokeResp = await client.PostAsJsonAsync($"{serverUrl}/api/auth/sessions/revoke", payload);
            if (revokeResp.IsSuccessStatusCode)
            {
                Console.WriteLine("Текущая сессия отозвана на сервере. Выход выполнен.");
            }
            else
            {
                Console.WriteLine($"Не удалось отозвать сессию на сервере: {revokeResp.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
        finally
        {
            ClearLocalAuth();
            client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static void ClearLocalAuth()
    {
        jwtToken = null;
        tokenExpires = default;
        currentUserEmail = null;
        currentUserId = null;
        refreshToken = null;
        currentSessionId = null;
    }

    private static async Task RegisterAsync()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Регистрация ===\n");
        Console.ResetColor();

        Console.Write("Email: ");
        var email = Console.ReadLine()?.Trim();

        Console.Write("Пароль (минимум 6 символов): ");
        var password = ReadPassword();

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Email и пароль обязательны!");
            Console.ResetColor();
            return;
        }

        var registerData = new
        {
            email = email,
            password = password
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                $"{serverUrl}/api/auth/register",
                registerData
            );

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                jwtToken = result.GetProperty("token").GetString();
                tokenExpires = result.GetProperty("expires").GetDateTime();
                currentUserEmail = result.GetProperty("email").GetString();
                currentUserId = result.GetProperty("userId").GetString();
                // new server returns refresh token and session id on register
                refreshToken = result.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
                currentSessionId = result.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Регистрация успешна!");
                Console.WriteLine($"User ID: {currentUserId}");
                Console.WriteLine($"Email: {currentUserEmail}");
                Console.WriteLine($"Token: {jwtToken.Substring(0, 50)}...");
                Console.WriteLine($"Expires: {tokenExpires}");
                if (!string.IsNullOrEmpty(refreshToken)) Console.WriteLine($"RefreshToken: {refreshToken.Substring(0, 50)}...");
                if (!string.IsNullOrEmpty(currentSessionId)) Console.WriteLine($"SessionId: {currentSessionId}");
                Console.ResetColor();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Ошибка регистрации: {response.StatusCode}");
                Console.WriteLine($"Детали: {error}");
                Console.ResetColor();
            }
        }
        catch (HttpRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n✗ Не удалось подключиться к серверу: {ex.Message}");
            Console.WriteLine($"Убедитесь, что сервер запущен на {serverUrl}");
            Console.ResetColor();
        }
    }

    private static async Task LoginAsync()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Вход ===\n");
        Console.ResetColor();

        Console.Write("Email: ");
        var email = Console.ReadLine()?.Trim();

        Console.Write("Пароль: ");
        var password = ReadPassword();

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Email и пароль обязательны!");
            Console.ResetColor();
            return;
        }

        var loginData = new
        {
            email = email,
            password = password
        };

        try
        {
            var response = await client.PostAsJsonAsync(
                $"{serverUrl}/api/auth/login",
                // include device info and ip so server can create a descriptive session
                new
                {
                    email = email,
                    password = password,
                    deviceInfo = Environment.MachineName + " / " + Environment.OSVersion.VersionString,
                    ip = GetLocalIpAddress()
                }
            );

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                jwtToken = result.GetProperty("token").GetString();
                tokenExpires = result.GetProperty("expires").GetDateTime();
                currentUserEmail = result.GetProperty("email").GetString();
                currentUserId = result.GetProperty("userId").GetString();
                // server now returns refresh token and session id on login
                refreshToken = result.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
                currentSessionId = result.TryGetProperty("sessionId", out var sid) ? sid.GetString() : null;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Вход выполнен успешно!");
                Console.WriteLine($"User ID: {currentUserId}");
                Console.WriteLine($"Email: {currentUserEmail}");
                Console.WriteLine($"Token: {jwtToken.Substring(0, 50)}...");
                Console.WriteLine($"Expires: {tokenExpires}");
                if (!string.IsNullOrEmpty(refreshToken)) Console.WriteLine($"RefreshToken: {refreshToken.Substring(0, 50)}...");
                if (!string.IsNullOrEmpty(currentSessionId)) Console.WriteLine($"SessionId: {currentSessionId}");
                Console.ResetColor();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Ошибка входа: {response.StatusCode}");
                Console.WriteLine($"Детали: {error}");
                Console.ResetColor();
            }
        }
        catch (HttpRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n✗ Не удалось подключиться к серверу: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static async Task TestProtectedEndpointAsync()
    {


        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Запрос к защищенному эндпоинту ===\n");
        Console.ResetColor();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwtToken);

        try
        {
            var response = await client.GetAsync($"{serverUrl}/api/test/protected");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Запрос успешен!");
                Console.WriteLine($"Message: {result.GetProperty("message").GetString()}");
                Console.WriteLine($"User ID: {result.GetProperty("userId").GetString()}");
                Console.WriteLine($"Email: {result.GetProperty("email").GetString()}");
                Console.ResetColor();
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Токен недействителен или истек. Необходимо войти заново.");
                jwtToken = null;
                currentUserEmail = null;
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка: {response.StatusCode}");
                Console.ResetColor();
            }
        }
        catch (HttpRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Ошибка подключения: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            client.DefaultRequestHeaders.Authorization = null;
        }
    }

    private static async Task TestPublicEndpointAsync()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("=== Запрос к публичному эндпоинту ===\n");
        Console.ResetColor();

        try
        {
            var response = await client.GetAsync($"{serverUrl}/api/test/public");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Запрос успешен!");
                Console.WriteLine($"Message: {result.GetProperty("message").GetString()}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"✗ Ошибка: {response.StatusCode}");
                Console.ResetColor();
            }
        }
        catch (HttpRequestException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Ошибка подключения: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void ShowTokenInfo()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Информация о токене ===\n");
        Console.ResetColor();

        if (string.IsNullOrEmpty(jwtToken))
        {
            Console.WriteLine("Токен отсутствует. Необходимо войти или зарегистрироваться.");
        }
        else
        {
            Console.WriteLine($"Token: {jwtToken}");
            Console.WriteLine($"Expires: {tokenExpires}");
            Console.WriteLine($"Time left: {(tokenExpires - DateTime.UtcNow).TotalHours:F1} hours");
            Console.WriteLine($"User: {currentUserEmail} (ID: {currentUserId})");
        }
    }

    private static string ReadPassword()
    {
        var password = string.Empty;
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                Console.Write("\b \b");
                password = password[0..^1];
            }
            else if (!char.IsControl(key.KeyChar))
            {
                Console.Write("*");
                password += key.KeyChar;
            }
        } while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return password;
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var addr in host.AddressList)
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
