namespace TEDxYouthYangikorgan
{
    class Program
    {
        static async Task Main(string[] args)
        {
            const string token = "7205228534:AAHILQ3v5UzohSksX2mX9Xou909u3ScxV5Y";

            BotHandler handler = new BotHandler(token);

            try
            {
                await handler.BotHandle();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
    }
}