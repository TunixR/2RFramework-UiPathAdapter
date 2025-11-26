using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[assembly: CollectionBehavior(DisableTestParallelization = true)] // Prevent parallel test interference with static capture list.

namespace _2RFramework.Activities.Utilities
{
    /// <summary>
    /// Tests for Action input simulation using capture/injection.
    /// Each test follows Arrange / Act / Assert pattern and relies on capture mode
    /// to avoid generating real OS input.
    /// </summary>
    public class ActionTests : IDisposable
    {
        // Reflection handle to private enum resolution method
        private static MethodInfo GetEnumFromStringMethod =>
            typeof(Action).GetMethod("GetEnumFromString", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("GetEnumFromString not found.");

        public ActionTests()
        {
            // Arrange common test environment (before each test)
            Action.EnableCapture(true);
            Action.SendInputFunc = (n, inputs, size) => 1; // Always 'success'; no real OS call.

            Action.ClearCaptured();
        }

        public void Dispose()
        {
            // Reset capture to avoid bleed between test collections (not strictly required here)
            Action.EnableCapture(false);
        }

        #region Helper Assertions

        private static void AssertKeyboardEvent(CapturedEvent e, ushort expectedVk, bool expectedKeyUp)
        {
            Assert.Equal("keyboard", e.Kind);
            Assert.True(e.Data.ContainsKey("vk"), "Missing vk");
            Assert.True(e.Data.ContainsKey("keyUp"), "Missing keyUp");
            Assert.Equal(expectedVk, Convert.ToUInt16(e.Data["vk"]));
            Assert.Equal(expectedKeyUp, Convert.ToBoolean(e.Data["keyUp"]));
        }

        private static void AssertMouseEvent(CapturedEvent e, uint expectedFlags, int? x = null, int? y = null, int? data = null)
        {
            Assert.Equal("mouse", e.Kind);
            Assert.Equal(expectedFlags, Convert.ToUInt32(e.Data["flags"]));
            if (x != null) Assert.Equal(x.Value, Convert.ToInt32(e.Data["x"]));
            if (y != null) Assert.Equal(y.Value, Convert.ToInt32(e.Data["y"]));
            if (data != null) Assert.Equal(data.Value, Convert.ToInt32(e.Data["data"]));
        }

        #endregion

        [Fact]
        public async void GetEnumFromString_ReturnsExpected_ForKnownValues()
        {
            // Arrange
            string[] known =
            {
                "hotkey","keydown","keyup","type","click","left_single",
                "left_double","right_single","hover","drag","select","scroll"
            };

            // Act & Assert
            foreach (var val in known)
            {
                var enumValue = GetEnumFromStringMethod.Invoke(null, new object[] { val });
                Assert.IsType<ActionType>(enumValue);
            }
        }

        [Fact]
        public async void GetEnumFromString_Throws_ForUnknownValue()
        {
            // Arrange
            string bad = "nonexistent_action_type";

            // Act
            var ex = Assert.Throws<TargetInvocationException>(() =>
                GetEnumFromStringMethod.Invoke(null, new object[] { bad }));

            // Assert
            Assert.IsType<ArgumentException>(ex.InnerException);
        }

        [Fact]
        public async void Hotkey_CtrlV_CapturesExpectedSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "hotkey", "ctrl v" }
            };

            // Act
            bool ok =  await Action.Parse("hotkey", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(4, events.Length);

            // ctrl (0x11) down
            AssertKeyboardEvent(events[0], 0x11, false);
            // v (0x56) down
            AssertKeyboardEvent(events[1], 0x56, false);
            // v up
            AssertKeyboardEvent(events[2], 0x56, true);
            // ctrl up
            AssertKeyboardEvent(events[3], 0x11, true);
        }

        [Fact]
        public async void Click_CapturesHoverAndDownUp()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ .100f, .200f } }
            };

            // Act
            bool ok =  await Action.Parse("click", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(3, events.Length);

            // MOVE|ABSOLUTE = 0x8001
            AssertMouseEvent(events[0], 0x8001);
            // Left down (0x0002)
            AssertMouseEvent(events[1], 0x0002);
            // Left up (0x0004)
            AssertMouseEvent(events[2], 0x0004);
        }

        [Fact]
        public async void DoubleClick_CapturesTwoClickSequences()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ .150f, .80f } }
            };

            // Act
            bool ok = await Action.Parse("left_double", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(6, events.Length);

            // First triple
            AssertMouseEvent(events[0], 0x8001);
            AssertMouseEvent(events[1], 0x0002);
            AssertMouseEvent(events[2], 0x0004);
            // Second triple
            AssertMouseEvent(events[3], 0x8001);
            AssertMouseEvent(events[4], 0x0002);
            AssertMouseEvent(events[5], 0x0004);
        }

        [Fact]
        public async void Drag_CapturesStartHover_Down_Move_Up()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ .10f, .20f } },
                { "end_box",   new List<object>{ .300f, .400f } }
            };

            // Act
            bool ok = await Action.Parse("drag", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(4, events.Length);

            // Sequence: hover(start), leftdown, move(end), leftup
            AssertMouseEvent(events[0], 0x8001);
            AssertMouseEvent(events[1], 0x0002);
            AssertMouseEvent(events[2], 0x8001);
            AssertMouseEvent(events[3], 0x0004);
        }

        [Fact]
        public async void Select_AliasOfDrag_SameSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ .5f, .5f } },
                { "end_box",   new List<object>{ .10f, .10f } }
            };

            // Act
            bool ok = await Action.Parse("select", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(4, events.Length);
            // Same pattern as drag
            AssertMouseEvent(events[0], 0x8001);
            AssertMouseEvent(events[1], 0x0002);
            AssertMouseEvent(events[2], 0x8001);
            AssertMouseEvent(events[3], 0x0004);
        }

        [Fact]
        public async void Scroll_Up_WithBox_CapturesHoverThenWheel()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "direction", "up" },
                { "start_box", new List<object>{ .250f, .125f } }
            };

            // Act
            bool ok = await Action.Parse("scroll", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(2, events.Length);

            // Hover then wheel (0x0800) with +120
            AssertMouseEvent(events[0], 0x8001);
            AssertMouseEvent(events[1], 0x0800, data: 120);
        }

        [Fact]
        public async void Scroll_Down_WithoutBox_CapturesOnlyWheel()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "direction", "down" }
            };

            // Act
            bool ok = await Action.Parse("scroll", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Single(events);

            AssertMouseEvent(events[0], 0x0800, data: -120);
        }

        [Fact]
        public async void Type_LowerAndUpperCharacters_CapturesShiftedSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "aA" }
            };

            // Act
            bool ok = await Action.Parse("type", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();

            // Expected sequence:
            // a down/up (0x61)
            // shift down
            // A down/up (0x41)
            // shift up
            Assert.Equal(4, events.Length);

            // a down/up
            AssertKeyboardEvent(events[0], 0x61, false);
            AssertKeyboardEvent(events[1], 0x61, true);
            // A down/up
            AssertKeyboardEvent(events[2], 0x41, false);
            AssertKeyboardEvent(events[3], 0x41, true);
        }

        [Fact]
        public async void Parse_KeyDown_And_KeyUp_ForSingleKey()
        {
            // Arrange
            var kdInputs = new Dictionary<string, object>
            {
                { "key", "enter" }
            };
            var kuInputs = new Dictionary<string, object>
            {
                { "key", "enter" }
            };

            // Act
            bool kdOk = await Action.Parse("keydown", JObject.FromObject(kdInputs));
            var afterKeyDown = Action.CapturedEvents.ToList();
            bool kuOk = await Action.Parse("keyup", JObject.FromObject(kuInputs));
            var afterKeyUp = Action.CapturedEvents.ToList();

            // Assert
            Assert.True(kdOk);
            Assert.True(kuOk);

            Assert.Single(afterKeyDown);
            AssertKeyboardEvent(afterKeyDown[0], 0x0D, false);

            Assert.Equal(2, afterKeyUp.Count);
            AssertKeyboardEvent(afterKeyUp[1], 0x0D, true);
        }

        [Fact]
        public async void Parse_KeyDown_UnknownKey_ThrowsArgumentException()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "key", "this_key_should_not_exist" }
            };

            // Act & Assert
            Assert.False(await Action.Parse("keydown", JObject.FromObject(inputs)));
        }

        [Fact]
        public async void Hover_AbsoluteCoordinates_UsesInjectedScreenSize()
        {
            // Arrange
            Action.ClearCaptured();
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ .500f, .100f } }
            };

            // Act 
            bool ok = await Action.Parse("hover", JObject.FromObject(inputs));

            // Assert
            Assert.True(ok);
            var single = Action.CapturedEvents.ToArray();
            var @event = Assert.Single(single);

            // Expected scaled coords:
            // X: .500 * 65535 ≈ 32767 (integer)
            // Y: .100 * 65535 ≈ 65535 (integer)
            int expectedX = (int)(.500f * 65535f);
            int expectedY = (int)(.100f * 65535f);

            AssertMouseEvent(@event, 0x8001, x: expectedX, y: expectedY);
        }

        [Fact]
        public async void Parse_Throws_ForUnknownActionType()
        {
            // Arrange
            var inputs = new Dictionary<string, object>();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => Action.Parse("nonexistent_action", JObject.FromObject(inputs)));
        }

        // EDGE CASES. Special chracters, Chinese, etc.

        [Fact]
        public async void Type_SpecialCharacters_CapturesExpectedKeyEvents()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "!@# $" }
            };
            // Act
            bool ok = await Action.Parse("type", JObject.FromObject(inputs));
            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();

            Assert.Equal(10, events.Length);
            // !
            AssertKeyboardEvent(events[0], 0x21, false); // ! down
            AssertKeyboardEvent(events[1], 0x21, true); // ! down
            // @
            AssertKeyboardEvent(events[2], 0x40, false);  // @ up
            AssertKeyboardEvent(events[3], 0x40, true);  // @ up
            // #
            AssertKeyboardEvent(events[4], 0x23, false); // # down
            AssertKeyboardEvent(events[5], 0x23, true);  // # up
            // space
            AssertKeyboardEvent(events[6], 0x20, false); // space down
            AssertKeyboardEvent(events[7], 0x20, true);  // space up
            // $
            AssertKeyboardEvent(events[8], 0x24, false); // $ down
            AssertKeyboardEvent(events[9], 0x24, true);  // $ up
        }

        [Fact]
        public async void Type_ChineseCharacters_CapturesExpectedKeyEvents()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "你好" } // "Hello" in Chinese
            };
            // Act
            bool ok = await Action.Parse("type", JObject.FromObject(inputs));
            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();

            Assert.True(events.Length == 4);
            // 你
            AssertKeyboardEvent(events[0], 0x4F60, false);
            AssertKeyboardEvent(events[1], 0x4F60, true);
            // 好
            AssertKeyboardEvent(events[2], 0x597D, false);
            AssertKeyboardEvent(events[3], 0x597D, true);
        }
    }
}
