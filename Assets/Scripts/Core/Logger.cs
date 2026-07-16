using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Statická třída pro zápis logů do souboru EVERLOST_LOGGER.log ve složce aplikace.
/// Umožňuje logování čtyř úrovní: INFO, WARNING, ERROR, DEBUG.
/// Zápis je thread-safe přes lock objekt. Soubor se při každém spuštění přepíše.
/// </summary>
public static class Logger
{
    /// <summary>
    /// Úroveň závažnosti log zprávy.
    /// </summary>
    public enum DEBUG_TYPE
    {
        INFO,
        WARNING,
        ERROR,
        DEBUG
    }

    /// <summary>Název výstupního souboru s logy.</summary>
    private static readonly string LogFileName = "EVERLOST_LOGGER.log";

    /// <summary>Formát časového razítka v log záznamu (rok-měsíc-den hodina:minuta:sekunda).</summary>
    private static readonly string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Objekt pro synchronizaci přístupu k souboru z více vláken.</summary>
    private static readonly object LockObject = new object();

    /// <summary>StreamWriter pro zápis do log souboru.</summary>
    private static StreamWriter _streamWriter;

    /// <summary>
    /// Statický konstruktor – automaticky se zavolá při prvním použití třídy.
    /// Inicializuje log soubor.
    /// </summary>
    static Logger()
    {
        InitializeLogFile();
    }

    /// <summary>
    /// Otevře nebo vytvoří log soubor pro zápis. Přepíše případný existující soubor.
    /// Pokud soubor nelze vytvořit, zaloguje chybu do Unity konzole.
    /// </summary>
    private static void InitializeLogFile()
    {
        try
        {
            _streamWriter = new StreamWriter(LogFileName, false)
            {
                AutoFlush = true
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize logger: {e.Message}");
        }
    }

    /// <summary>
    /// Zapíše zprávu do souboru s časovým razítkem a typem zprávy.
    /// Současně vypíše zprávu do Unity konzole přes Debug.Log.
    /// Zápis do souboru je chráněn zámkem pro bezpečné volání z více vláken.
    /// </summary>
    /// <param name="message">Text zprávy k zapsání.</param>
    /// <param name="type">Úroveň závažnosti (výchozí INFO).</param>
    public static void Log(string message, DEBUG_TYPE type = DEBUG_TYPE.INFO)
    {
        string logMessage = $"[{DateTime.Now.ToString(TimeFormat)}] [{type}] : {message}";
        Debug.Log(message);

        lock (LockObject)
        {
            _streamWriter?.WriteLine(logMessage);
        }
    }

    /// <summary>Zapíše zprávu s úrovní ERROR.</summary>
    public static void LogError(string message) => Log(message, DEBUG_TYPE.ERROR);

    /// <summary>Zapíše zprávu s úrovní WARNING.</summary>
    public static void LogWarning(string message) => Log(message, DEBUG_TYPE.WARNING);

    /// <summary>Zapíše zprávu s úrovní DEBUG.</summary>
    public static void LogDebug(string message) => Log(message, DEBUG_TYPE.DEBUG);

    /// <summary>
    /// Bezpečně uzavře stream a uvolní prostředky.
    /// Volat při ukončení aplikace, pokud je logger stále aktivní.
    /// </summary>
    public static void Close()
    {
        lock (LockObject)
        {
            _streamWriter?.Dispose();
            _streamWriter = null;
        }
    }
}