namespace tyfapi.cli.Tyfapi;

public class TyfapiConsole
{
    public static void Run(string[] args)
    {
        // Check if arguments are provided
        if (args == null || args.Length == 0)
        {
            Console.WriteLine("No arguments provided. Usage: tyfapi PATH");
            return;
        }

        // First argument should be the path to the file
        string filePath = args[0];

        try
        {
            if (File.Exists(filePath))
            {
                Console.WriteLine("file loaded");
            }
            else
            {
                Console.WriteLine("failed");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("failed");
        }
    }
}
