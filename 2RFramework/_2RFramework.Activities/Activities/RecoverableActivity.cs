using System;
using System.Activities;
using System.Activities.Tracking;
using System.Collections.Generic;

namespace _2RFramework.Activities.Activities
{
    public sealed class RecoverableActivity : NativeActivity
    {
        // The child activity to run
        public InArgument<Activity> Child { get; set; }

        // Optional: a logical id for the bookmark (useful if you have multiple)
        public InArgument<string> BookmarkId { get; set; } = new InArgument<string>(env => Guid.NewGuid().ToString("N"));

        // Internal state
        private ActivityInstance _childInstance;
        private string _bookmarkName => BookmarkId.Expression != null ? BookmarkId.Expression.ToString() : null;

        protected override void Execute(NativeActivityContext context)
        {
            var activity = Child.Get(context);
            if (activity == null)
                return;

            // schedule the child; hooking completion and fault handlers
            context.ScheduleActivity(activity, OnChildCompleted, OnChildFaulted);
        }

        private void OnChildCompleted(NativeActivityContext context, ActivityInstance completedInstance)
        {
            // child succeeded -> nothing special to do, the wrapper completes and parent continues
        }

        private void OnChildFaulted(NativeActivityFaultContext faultContext, Exception propagatedException, ActivityInstance propagatedFrom)
        {
            // mark the fault as handled so the engine doesn't abort the parent
            faultContext.HandleFault();

            var bookmarkId = BookmarkId.Get(faultContext) ?? Guid.NewGuid().ToString("N");
            var bookmarkName = $"recoverable_{bookmarkId}";

            // create the bookmark and return control to the host. The host should ResumeBookmark(bookmarkName, command)
            faultContext.CreateBookmark(bookmarkName, OnResumeBookmark);
        }

        private void OnResumeBookmark(NativeActivityContext context, Bookmark bookmark, object value)
        {
            // value: the host-supplied resume argument; we expect a string command or a simple object
            // examples: "retry", "skip", or a richer object with variables to apply
            if (value is string s)
            {
                if (string.Equals(s, "retry", StringComparison.OrdinalIgnoreCase))
                {
                    // retry: schedule the same child again (we fetch it from the input argument)
                    var activity = Child.Get(context);
                    if (activity != null)
                    {
                        context.ScheduleActivity(activity, OnChildCompleted, OnChildFaulted);
                        return;
                    }
                }
                else if (string.Equals(s, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    // skip: do nothing, wrapper completes and parent continues
                    return;
                }
            }

            // if value is a dictionary instructing to modify variables, you can apply them to the ambient variables,
            // or if you don't recognize the command, default to skip to avoid stalling.
        }
    }
}

