using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

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
            // Deterministic screen size for absolute coordinate calculations
            Action.ScreenWidthFunc = () => 1000;
            Action.ScreenHeightFunc = () => 500;

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
        public void GetEnumFromString_ReturnsExpected_ForKnownValues()
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
        public void GetEnumFromString_Throws_ForUnknownValue()
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
        public void Hotkey_CtrlV_CapturesExpectedSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "hotkey", "ctrl v" }
            };

            // Act
            bool ok = Action.Parse("hotkey", inputs);

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
        public void Click_CapturesHoverAndDownUp()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "box", new List<object>{ 100f, 200f } }
            };

            // Act
            bool ok = Action.Parse("click", inputs);

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
        public void DoubleClick_CapturesTwoClickSequences()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "box", new List<object>{ 150f, 80f } }
            };

            // Act
            bool ok = Action.Parse("left_double", inputs);

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
        public void Drag_CapturesStartHover_Down_Move_Up()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ 10f, 20f } },
                { "end_box",   new List<object>{ 300f, 400f } }
            };

            // Act
            bool ok = Action.Parse("drag", inputs);

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
        public void Select_AliasOfDrag_SameSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "start_box", new List<object>{ 5f, 5f } },
                { "end_box",   new List<object>{ 10f, 10f } }
            };

            // Act
            bool ok = Action.Parse("select", inputs);

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
        public void Scroll_Up_WithBox_CapturesHoverThenWheel()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "direction", "up" },
                { "box", new List<object>{ 250f, 125f } }
            };

            // Act
            bool ok = Action.Parse("scroll", inputs);

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Equal(2, events.Length);

            // Hover then wheel (0x0800) with +120
            AssertMouseEvent(events[0], 0x8001);
            AssertMouseEvent(events[1], 0x0800, data: 120);
        }

        [Fact]
        public void Scroll_Down_WithoutBox_CapturesOnlyWheel()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "direction", "down" }
            };

            // Act
            bool ok = Action.Parse("scroll", inputs);

            // Assert
            Assert.True(ok);
            var events = Action.CapturedEvents.ToArray();
            Assert.Single(events);

            AssertMouseEvent(events[0], 0x0800, data: -120);
        }

        [Fact]
        public void Type_LowerAndUpperCharacters_CapturesShiftedSequence()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "aA" }
            };

            // Act
            bool ok = Action.Parse("type", inputs);

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
        public void Parse_KeyDown_And_KeyUp_ForSingleKey()
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
            bool kdOk = Action.Parse("keydown", kdInputs);
            var afterKeyDown = Action.CapturedEvents.ToList();
            bool kuOk = Action.Parse("keyup", kuInputs);
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
        public void Parse_KeyDown_UnknownKey_ThrowsArgumentException()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "key", "this_key_should_not_exist" }
            };

            // Act & Assert
            Assert.False(Action.Parse("keydown", inputs));
        }

        [Fact]
        public void Hover_AbsoluteCoordinates_UsesInjectedScreenSize()
        {
            // Arrange
            Action.ClearCaptured();
            var inputs = new Dictionary<string, object>
            {
                { "box", new List<object>{ 50f, 100f } }
            };

            // Act 
            bool ok = Action.Parse("hover", inputs);

            // Assert
            Assert.True(ok);
            var single = Action.CapturedEvents.ToArray();
            var @event = Assert.Single(single);

            // Expected scaled coords:
            // X: (50 / 1000) * 65535 ≈ 3276 (integer)
            // Y: (100 / 500) * 65535 ≈ 13107
            int expectedX = (int)(50f * 65535f / 1000f);
            int expectedY = (int)(100f * 65535f / 500f);

            AssertMouseEvent(@event, 0x8001, x: expectedX, y: expectedY);
        }

        [Fact]
        public void Parse_Throws_ForUnknownActionType()
        {
            // Arrange
            var inputs = new Dictionary<string, object>();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => Action.Parse("unknown_action_type", inputs));
        }

        // EDGE CASES. Special chracters, Chinese, etc.

        [Fact]
        public void Type_SpecialCharacters_CapturesExpectedKeyEvents()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "!@# $" }
            };
            // Act
            bool ok = Action.Parse("type", inputs);
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
        public void Type_ChineseCharacters_CapturesExpectedKeyEvents()
        {
            // Arrange
            var inputs = new Dictionary<string, object>
            {
                { "content", "你好" } // "Hello" in Chinese
            };
            // Act
            bool ok = Action.Parse("type", inputs);
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
