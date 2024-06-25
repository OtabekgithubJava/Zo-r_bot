namespace TEDxYouthYangikorgan
{
    class Program
    {
        static async Task Main(string[] args)
        {
            const string token = "Token shu yerga";

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
