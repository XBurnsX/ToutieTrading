using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ToutieTrader.Core.Interfaces;

namespace ToutieTrader.Core.Engine;

/// <summary>
/// Charge et compile les fichiers .cs du dossier /Strategies/ via Roslyn.
///
/// Plug & Play : nouvelle Strategy = glisser un .cs + redémarrer.
/// Zéro Visual Studio requis pour ajouter une Strategy.
///
/// Erreur de compilation → loggée via OnCompilationError, bot continue sans cette Strategy.
/// </summary>
public sealed class StrategyLoader
{
    private readonly string _strategiesPath;

    /// <summary>Appelé quand un fichier .cs échoue la compilation. Param = (nomFichier, erreurs).</summary>
    public event Action<string, string>? OnCompilationError;

    /// <summary>Appelé pour chaque Strategy chargée avec succès. Param = nomFichier.</summary>
    public event Action<string>? OnStrategyLoaded;

    public StrategyLoader(string strategiesPath)
    {
        _strategiesPath = strategiesPath;
    }

    /// <summary>
    /// Scanne le dossier /Strategies/, compile chaque .cs et retourne les instances IStrategy valides.
    /// </summary>
    public List<IStrategy> LoadAll()
    {
        var strategies = new List<IStrategy>();

        if (!Directory.Exists(_strategiesPath))
        {
            Directory.CreateDirectory(_strategiesPath);
            return strategies;
        }

        var csFiles = Directory.GetFiles(_strategiesPath, "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var filePath in csFiles)
        {
            var strategy = TryCompileAndLoad(filePath);
            if (strategy is not null)
            {
                strategies.Add(strategy);
                OnStrategyLoaded?.Invoke(Path.GetFileName(filePath));
            }
        }

        return strategies;
    }

    // ─── Compilation Roslyn ───────────────────────────────────────────────────

    private IStrategy? TryCompileAndLoad(string filePath)
    {
        string source;
        try
        {
            source = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            OnCompilationError?.Invoke(Path.GetFileName(filePath), $"Lecture impossible : {ex.Message}");
            return null;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileNameWithoutExtension(filePath),
            syntaxTrees:  [syntaxTree],
            references:   references,
            options:      new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  [{d.Id}] {d.GetMessage()}"));

            OnCompilationError?.Invoke(Path.GetFileName(filePath), errors);
            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        var strategyType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IStrategy).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        if (strategyType is null)
        {
            OnCompilationError?.Invoke(
                Path.GetFileName(filePath),
                "Aucune classe implémentant IStrategy trouvée dans ce fichier.");
            return null;
        }

        try
        {
            return (IStrategy?)Activator.CreateInstance(strategyType);
        }
        catch (Exception ex)
        {
            OnCompilationError?.Invoke(
                Path.GetFileName(filePath),
                $"Erreur instanciation : {ex.Message}");
            return null;
        }
    }

    // ─── Références Roslyn ────────────────────────────────────────────────────

    private static List<MetadataReference> BuildReferences()
    {
        // Assemblies du runtime courant + assembly Core (IStrategy, models)
        var refs = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // S'assurer que le Core est bien inclus
        var coreLocation = typeof(IStrategy).Assembly.Location;
        if (!refs.Any(r => r is PortableExecutableReference pe && pe.FilePath == coreLocation))
            refs.Add(MetadataReference.CreateFromFile(coreLocation));

        return refs;
    }
}
