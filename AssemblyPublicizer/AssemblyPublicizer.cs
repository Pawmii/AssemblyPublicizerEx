using System.CommandLine;
using Mono.Cecil;

var rootCommand = new RootCommand("AssemblyPublicizer - Makes all members of a .NET assembly public");

var inputOption = new Option<FileInfo>(
    aliases: ["--input", "-i"],
    description: "Path to input assembly"
) { IsRequired = true };

var outputOption = new Option<string>(
    aliases: ["--output", "-o"],
    description: "Path for output assembly"
);

var libsOption = new Option<string>(
    aliases: ["--libs", "-l"],
    description: "Path to dependencies directory"
);

var fullExceptionOption = new Option<bool>(
    aliases: ["--fullexceptions", "-fe"],
    description: "Show full exception stack trace"
);

var autoExitOption = new Option<bool>(
    aliases: ["--exit", "-e"],
    description: "Exit automatically without waiting for key"
);

rootCommand.AddOption(inputOption);
rootCommand.AddOption(outputOption);
rootCommand.AddOption(libsOption);
rootCommand.AddOption(fullExceptionOption);
rootCommand.AddOption(autoExitOption);

rootCommand.SetHandler(async (input, output, libs, fullExceptions, autoExit) =>
{
    const string suffix = "_publicized";
    const string defaultOutputDir = "publicized_assemblies";

    if (!input.Exists)
    {
        Console.WriteLine("ERROR! File doesn't exist.");
        Exit(autoExit, 1);
    }

    string outputPath = "";
    string outputName = "";

    if (!string.IsNullOrWhiteSpace(output))
    {
        outputPath = Path.GetDirectoryName(output);
        outputName = Path.GetFileName(output);
    }

    AssemblyDefinition assembly;

    try
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(!string.IsNullOrEmpty(libs) ? libs : input.DirectoryName);

        var reader = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = false
        };

        assembly = AssemblyDefinition.ReadAssembly(input.FullName, reader);
    }
    catch (Exception ex)
    {
        Console.WriteLine("ERROR! Failed to load assembly.");
        if (fullExceptions)
            Console.WriteLine(ex);
        Exit(autoExit, 2);
        return;
    }

    var allTypes = GetAllTypes(assembly.MainModule);
    var allMethods = allTypes.SelectMany(t => t.Methods);
    var allFields = allTypes.SelectMany(t => t.Fields);

    int count = 0;
    
    foreach (var type in allTypes)
    {
        if (type is { IsPublic: false, IsNestedPublic: false })
        {
            count++;
            if (type.IsNested)
                type.IsNestedPublic = true;
            else
                type.IsPublic = true;
        }
    }
    Console.WriteLine($"Changed {count} types to public.");

    count = 0;
    
    int getters = 0;
    int setters = 0;
    
    foreach (var method in allMethods)
    {
        if (!method.IsPublic)
        {
            count++;

            if (method.Name.StartsWith("get_"))
                getters++;
            else if (method.Name.StartsWith("set_"))
                setters++;
            
            method.IsPublic = true;
        }
    }
    Console.WriteLine($"Changed {count} methods to public of which {getters} are getters and {setters} are setters.");

    count = 0;
    foreach (var field in allFields)
    {
        if (!field.IsPublic && field.DeclaringType.Events.All(e => e.Name != field.Name))
        {
            count++;
            field.IsPublic = true;
        }
    }
    Console.WriteLine($"Changed {count} fields to public.");

    if (string.IsNullOrWhiteSpace(outputName))
    {
        outputName = Path.GetFileNameWithoutExtension(input.Name) + suffix + input.Extension;
        Console.WriteLine($"Using default output name: {outputName}");
    }

    if (string.IsNullOrWhiteSpace(outputPath))
    {
        outputPath = defaultOutputDir;
        Console.WriteLine($"Using default output directory: {outputPath}");
    }

    var finalPath = Path.Combine(outputPath, outputName);

    try
    {
        Directory.CreateDirectory(outputPath);
        assembly.Write(finalPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine("ERROR! Failed to save modified assembly.");
        if (fullExceptions)
            Console.WriteLine(ex);
        Exit(autoExit, 3);
    }

    Console.WriteLine("Publicization complete.");
    Exit(autoExit, 0);
}, inputOption, outputOption, libsOption, fullExceptionOption, autoExitOption);

return await rootCommand.InvokeAsync(args);

static void Exit(bool autoExit, int code)
{
    if (!autoExit)
    {
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
    Environment.Exit(code);
}

static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition module)
{
    return GetAllNestedTypes(module.Types);
}

static IEnumerable<TypeDefinition> GetAllNestedTypes(IEnumerable<TypeDefinition> types)
{
    if (!types.Any()) 
        return [];
    
    return types.Concat(GetAllNestedTypes(types.SelectMany(t => t.NestedTypes)));
}