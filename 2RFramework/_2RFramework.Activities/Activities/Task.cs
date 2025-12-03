using _2RFramework.Activities.Properties;
using _2RFramework.Activities.Utilities;
using _2RFramework.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UiPath.Shared.Activities.Localization;
using Activity = System.Activities.Activity;
using ThreadingTask = System.Threading.Tasks.Task;

namespace _2RFramework.Activities;

/// <summary>
///     Represents a custom activity that executes a collection of child activities in sequence.
/// </summary>
[LocalizedDisplayName(nameof(Resources.Task_DisplayName))]
[LocalizedDescription(nameof(Resources.Task_Description))]
public class Task : NativeActivity
{
    #region Properties

    /// <summary>
    ///     Gets or sets the name of the task.
    /// </summary>
    [Browsable(true)]
    [LocalizedCategory(nameof(Resources.Common_Category))]
    [DisplayName("Task Name")]
    [Description("Name of the task")]
    public InArgument<String> TaskName { get; set; } = new();

    /// <summary>
    ///     Collection of activities that will be executed by this task.
    ///     This allows multiple activities without using a Sequence container.
    /// </summary>
    [Browsable(true)]
    [LocalizedCategory(nameof(Resources.Common_Category))]
    [DisplayName("Activities")]
    [Description("Collection of activities to be executed by this task")]
    public List<Activity> Activities { get; set; } = new();

    // Object Container: Add strongly-typed objects here and they will be available in the scope's child activities.
    private int _currentActivityIndex;

    #endregion

    #region Protected Methods

    /// <inheritdoc />
    protected override void Execute(NativeActivityContext context)
    {
        // Load environment variables from .env file
        EnvReader.Load(".env");

        // If there are no activities, just return
        if (Activities == null || Activities.Count == 0) return;

        // Reset the index and start execution
        _currentActivityIndex = 0;
        ScheduleNext(context);
    }

    #endregion


    #region Helpers

    private void ScheduleNext(NativeActivityContext context)
    {
        if (_currentActivityIndex < Activities.Count)
        {
            // Schedule the activity with handlers for completion and faulting
            context.ScheduleActivity(Activities[_currentActivityIndex], OnCompleted, OnFaulted);
        }
    }

    #endregion


    #region Events

    /// <summary>
    ///     In this method, we recollect task and process data to invoke 2RAgent via API, which must then try and resolve the
    ///     error.
    ///     If the error is resolved, we can continue to the next activity.
    /// </summary>
    /// <param name="faultContext"></param>
    /// <param name="propagatedException"></param>
    /// <param name="propagatedFrom"></param>
    private void OnFaulted(NativeActivityFaultContext faultContext, Exception propagatedException,
        ActivityInstance propagatedFrom)
    {
        var taskNameValue = TaskName.Get(faultContext);

        // Get workflow variables using the utility method
        var workflowVariables = TaskUtils.GetWorkflowVariables(this);

        // We want the following properties of activities:
        // - Index
        // - Type (e.g., Write Line, If, etc.)
        // - Attributes (e.g., MessageBox text, If condition, etc.)
        var previousActivities = Activities.Take(_currentActivityIndex)
            .Select(a => TaskUtils.GetActivityInfo(a, workflowVariables))
            .ToList();
        var failedActivity = TaskUtils.GetActivityInfo(Activities[_currentActivityIndex], workflowVariables);
        var reversedAct = new List<Activity>(Activities);
        reversedAct.Reverse();
        var futureActivities = reversedAct.Take(Activities.Count() - _currentActivityIndex - 1) // Exclude current failed activity
            .Select(a => TaskUtils.GetActivityInfo(a, workflowVariables))
            .ToList();
        futureActivities.Reverse();

        var message = new
        {
            code = propagatedException.Message,
            variables = workflowVariables,
            details = new
            {
                taskName = taskNameValue,
                previousActivities,
                failedActivity,
                futureActivities,
            }
        };

        string apiEndpoint = Environment.GetEnvironmentVariable("API_ENDPOINT");
        Console.WriteLine($"Calling Recovery API at: {apiEndpoint}");
        var response = ThreadingTask.Run(() => TaskUtils.CallRecoveryAPIAsync(message, apiEndpoint, null)).GetAwaiter().GetResult();
        Console.WriteLine($"Recovery API response: {JObject.FromObject(response).ToString()}");

        if ((string)response["type"] == "error")
        {
            throw new ApplicationException("Error could not be resolved by Recovery API.");
        } else if ((string)response["type"] == "done")
        {
            var content = response["content"];
            // We grab now success and from. If success is true, we continue from the specified future activity index
            var success = (bool)content.GetType().GetProperty("success").GetValue(content, null);
            var from = (int)content.GetType().GetProperty("continue_from_step").GetValue(content, null);
            if (success)
            {
            // Mark the exception as handled
            faultContext.HandleFault();
            if (from < 0 || from + _currentActivityIndex > Activities.Count())
                {
                    _currentActivityIndex = Activities.Count(); // End the task execution
                } else
                {
                    _currentActivityIndex = _currentActivityIndex + from; // Here we do not increment currentActivityIndex by one extra because OnCompleted will be called
                }
            }
            else
            {
                var ogErr = propagatedException.Message;
                throw new ApplicationException($"Error could not be resolved by Recovery API. Original Error: {ogErr}");
            }
        }
        else
        {
            var ogErr = propagatedException.Message;
            throw new ApplicationException($"Unknown response type from Recovery API. Original Error: {ogErr}");
        }
    }

    private void OnCompleted(NativeActivityContext context, ActivityInstance completedInstance)
    {
        // Move to the next activity and schedule it
        _currentActivityIndex++;
        ScheduleNext(context);
    }

    #endregion
}