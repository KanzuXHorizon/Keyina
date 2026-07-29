namespace Keyina.BrandAssets;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var command = args.Length > 0 ? args[0] : string.Empty;
            var root = ReadRoot(args);
            switch (command)
            {
                case "catalog":
                    ConceptCatalog.Generate(root);
                    Console.WriteLine("Generated docs/brand/concept-assets.json");
                    return 0;
                case "vectors":
                    SvgWriter.GenerateAll(root);
                    Console.WriteLine("Generated Keyina vector brand sources");
                    return 0;
                case "generate":
                    ConceptCatalog.Generate(root);
                    SvgWriter.GenerateAll(root);
                    RasterWriter.GenerateAll(root);
                    Console.WriteLine("Generated complete Keyina brand asset set");
                    return 0;
                default:
                    Console.Error.WriteLine(
                        "Usage: Keyina.BrandAssets <catalog|vectors|generate> --root <repository-root>");
                    return 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ReadRoot(string[] args)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--root", StringComparison.Ordinal))
            {
                var root = Path.GetFullPath(args[index + 1]);
                if (!File.Exists(Path.Combine(root, "Keyina.slnx")))
                {
                    throw new DirectoryNotFoundException(
                        $"The specified root is not a Keyina repository: {root}");
                }
                return root;
            }
        }

        throw new ArgumentException("Missing required option: --root <repository-root>");
    }
}
