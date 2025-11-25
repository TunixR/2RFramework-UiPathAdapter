using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Activity = System.Activities.Activity;
using System.Drawing;


namespace _2RFramework.Activities.Utilities;

/// <summary>
///     Provides utility methods for workflow activities and task management.
/// </summary>
internal static class TaskUtils
{
    private static readonly List<string> ExcludedProperties = new() { "Result", "ResultType", "Id" };

    /// <summary>
    ///     Extracts activity information including properties and their values.
    /// </summary>
    /// <param name="activity">The activity to extract information from.</param>
    /// <param name="variables">Dictionary of workflow variables.</param>
    /// <returns>A list of objects containing activity information.</returns>
    public static List<object> GetActivityInfo(Activity activity, Dictionary<string, object?> variables)
    {
        var type = activity.GetType();
        var activityInfo = new List<object>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (ExcludedProperties.Contains(prop.Name))
                continue;

            var value = prop.GetValue(activity);

            // Handle InArgument<T> and extract its value
            if (value is Argument argument)
                value = ExtractArgumentValue(argument, variables);

            if (value == null)
                continue;

            if (prop.Name == "DisplayName")
                // Activity type goes at the top of the list
                activityInfo.Insert(0, new { ActivityType = value });
            else
                activityInfo.Add(new { PropertyName = prop.Name, Value = value });
        }

        return activityInfo;
    }

    /// <summary>
    ///     Attempts to extract a literal value or variable reference from an argument.
    /// </summary>
    /// <param name="argument">The argument to extract value from.</param>
    /// <param name="variables">Dictionary of workflow variables.</param>
    /// <returns>The extracted value or null if extraction fails.</returns>
    private static object? ExtractArgumentValue(Argument argument, Dictionary<string, object?> variables)
    {
        var expressionObject = argument.Expression;

        // Check if it's a Literal<T> by looking for the Value property
        var valueProperty = expressionObject?.GetType().GetProperty("Value");
        if (valueProperty != null)
            return valueProperty.GetValue(expressionObject)?.ToString();

        // Try to get the ExpressionText property (for text and selection entries)
        var expressionTextProperty = expressionObject?.GetType().GetProperty("ExpressionText");
        if (expressionTextProperty == null) return null;

        var expressionText = expressionTextProperty.GetValue(expressionObject) as string;
        if (string.IsNullOrEmpty(expressionText)) return null;

        // Distinguish between string literals and variable references
        return IsStringLiteral(expressionText) || !variables.ContainsKey(expressionText)
            ? expressionText
            : CreateVariableReference(expressionText, variables);
    }

    /// <summary>
    ///     Determines if the expression text represents a string literal (wrapped in quotes).
    /// </summary>
    /// <param name="expressionText">The expression text to check.</param>
    /// <returns>True if the text is a string literal; otherwise, false.</returns>
    private static bool IsStringLiteral(string expressionText)
    {
        return expressionText.StartsWith("\"") && expressionText.EndsWith("\"");
    }

    /// <summary>
    ///     Creates a variable reference object with name and value.
    /// </summary>
    /// <param name="variableName">The name of the variable.</param>
    /// <param name="variables">Dictionary of workflow variables.</param>
    /// <returns>An anonymous object representing the variable reference.</returns>
    private static object CreateVariableReference(string variableName, Dictionary<string, object?> variables)
    {
        return new
        {
            Type = "VariableReference",
            VariableName = variableName,
            Value = variables.GetValueOrDefault(variableName)
        };
    }

    /// <summary>
    ///     Gets all activity descendants from a root activity.
    /// </summary>
    /// <param name="root">The root activity.</param>
    /// <param name="recursive">Whether to get descendants recursively.</param>
    /// <returns>An enumerable of activities.</returns>
    public static IEnumerable<Activity> GetDescendants(Activity root, bool recursive)
    {
        yield return root;

        foreach (var child in WorkflowInspectionServices.GetActivities(root))
            if (recursive)
                foreach (var descendant in GetDescendants(child, true))
                    yield return descendant;
            else
                yield return child;
    }

    /// <summary>
    ///     Gets all workflow variables from the main sequence as a dictionary.
    /// </summary>
    /// <param name="activity">A native activity.</param>
    /// <returns>Dictionary of variable names and their values.</returns>
    public static Dictionary<string, object?> GetWorkflowVariables(Activity activity)
    {
        var workflowVariables = new Dictionary<string, object?>();

        foreach (var local in activity.GetLocals())
            try
            {
                // Get the variable property (first property of the local)
                var variableProperty = local.GetType().GetProperties().FirstOrDefault();

                var variable = variableProperty?.GetValue(local);

                // Get the Value property of the variable
                var valueProperty = variable?.GetType().GetProperty("Value");
                if (valueProperty == null)
                    continue;

                var value = valueProperty.GetValue(variable);
                workflowVariables.Add(local.Name, value);
            }
            catch (Exception)
            {
                // Skip variables that can't be processed
            }

        return workflowVariables;
    }

    public static async Task<object> CallRecoveryAPIAsync(object message, string uri, params object[]? args)
    {
        // Convert http/https to ws(s) if needed
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            uri = "ws://" + uri.Substring("http://".Length);
        else if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            uri = "wss://" + uri.Substring("https://".Length);

        using var cts = new CancellationTokenSource();
        using var ws = new ClientWebSocket();

        try
        {
            await ws.ConnectAsync(new Uri(uri), cts.Token).ConfigureAwait(false);

            // Send the initial JSON message
            var content = JsonConvert.SerializeObject(message);
            var contentBytes = Encoding.UTF8.GetBytes(content);
            await ws.SendAsync(new ArraySegment<byte>(contentBytes), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

            var buffer = new byte[8192]; // 8 KB buffer

            while (ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cts.Token).ConfigureAwait(false);
                        return new { Type = "closed" };
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Position = 0;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    using var reader = new StreamReader(ms, Encoding.UTF8);
                    var messageText = await reader.ReadToEndAsync().ConfigureAwait(false);

                    JObject? json;
                    try
                    {
                        json = JObject.Parse(messageText);
                    }
                    catch (JsonException)
                    {
                        // Not JSON — return raw text
                        continue;
                    }

                    var typeToken = json["type"] ?? json["Type"];
                    var type = typeToken?.ToString()?.ToLowerInvariant();

                    if (type == "done")
                    {
                        return json;
                    }
                    else if (type == "code")
                    {
                        // TODO: implement handling for "code" messages
                        // Leave blank for user logic
                    }
                    else if (type == "screenshot")
                    {
                        var pngBytes = CaptureScreenPng();
                        if (pngBytes.Length > 0)
                            await ws.SendAsync(new ArraySegment<byte>(pngBytes), WebSocketMessageType.Binary, true, cts.Token).ConfigureAwait(false);
                    }
                }
            }

            return new { Type = "closed" };
        }
        finally
        {
            try
            {
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static byte[] CaptureScreenPng()
    {
        try
        {
            Image screen = Pranas.ScreenshotCapture.TakeScreenshot(true);

            using (var ms = new MemoryStream())
            {
                screen.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
        }
        catch { return Array.Empty<byte>(); }
    }
}