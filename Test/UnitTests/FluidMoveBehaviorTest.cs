// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xaml.Behaviors;
using Microsoft.Xaml.Behaviors.Layout;

namespace Microsoft.Xaml.Interactions.UnitTests
{
    [TestClass]
    public class FluidMoveBehaviorTest
    {
        [TestInitialize]
        public void Setup()
        {
            // Clear static dictionaries before each test to avoid cross-test contamination.
            FluidMoveBehaviorBase.TagDictionary.Clear();
            FluidMoveBehavior.ClearStoryboardDictionary();
        }

        #region TagData WeakReference Tests

        [TestMethod]
        public void TagData_Child_ReturnsElementWhileAlive()
        {
            var element = new Button();
            var tagData = new FluidMoveBehaviorBase.TagData { Child = element };

            Assert.AreSame(element, tagData.Child);
        }

        [TestMethod]
        public void TagData_Parent_ReturnsElementWhileAlive()
        {
            var element = new StackPanel();
            var tagData = new FluidMoveBehaviorBase.TagData { Parent = element };

            Assert.AreSame(element, tagData.Parent);
        }

        [TestMethod]
        public void TagData_IsAlive_ReturnsTrueWhileChildAlive()
        {
            var element = new Button();
            var tagData = new FluidMoveBehaviorBase.TagData { Child = element };

            Assert.IsTrue(tagData.IsAlive);
            GC.KeepAlive(element);
        }

        [TestMethod]
        public void TagData_SetChildNull_ReturnsNull()
        {
            var tagData = new FluidMoveBehaviorBase.TagData { Child = new Button() };
            tagData.Child = null;

            Assert.IsNull(tagData.Child);
            Assert.IsFalse(tagData.IsAlive);
        }

        [TestMethod]
        public void TagData_Child_ReturnsNullAfterGC()
        {
            var tagData = new FluidMoveBehaviorBase.TagData();
            SetChildToEphemeralElement(tagData);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsNull(tagData.Child);
        }

        [TestMethod]
        public void TagData_Parent_ReturnsNullAfterGC()
        {
            var tagData = new FluidMoveBehaviorBase.TagData();
            SetParentToEphemeralElement(tagData);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsNull(tagData.Parent);
        }

        [TestMethod]
        public void TagData_IsAlive_ReturnsFalseAfterGC()
        {
            var tagData = new FluidMoveBehaviorBase.TagData();
            SetChildToEphemeralElement(tagData);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(tagData.IsAlive);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetChildToEphemeralElement(FluidMoveBehaviorBase.TagData tagData)
        {
            tagData.Child = new Button();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SetParentToEphemeralElement(FluidMoveBehaviorBase.TagData tagData)
        {
            tagData.Parent = new StackPanel();
        }

        #endregion

        #region TagDictionary Purge Tests

        [TestMethod]
        public void TagDictionary_PurgesEntryWhenChildCollected()
        {
            // Simulate a DataContext-type tag: the key is a data object, the element is in the value.
            string dataContextKey = "item1";
            AddEphemeralTagDataEntry(dataContextKey);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // The WeakReference should be dead now.
            Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(dataContextKey),
                "Entry should still exist in dictionary before purge.");

            FluidMoveBehaviorBase.TagData td;
            FluidMoveBehaviorBase.TagDictionary.TryGetValue(dataContextKey, out td);
            Assert.IsFalse(td.IsAlive, "Child should no longer be alive after GC.");

            // Simulate purge: remove dead entries.
            PurgeDeadTagEntries();

            Assert.IsFalse(FluidMoveBehaviorBase.TagDictionary.ContainsKey(dataContextKey),
                "Entry should be removed after purge.");
        }

        [TestMethod]
        public void TagDictionary_PurgesEntryWhenElementKeyUnloaded()
        {
            // Simulate an Element-type tag: the key IS the FrameworkElement.
            // An element never added to a visual tree has IsLoaded == false.
            var element = new Button();
            var tagData = new FluidMoveBehaviorBase.TagData
            {
                Child = element,
                Parent = null,
                ParentRect = Rect.Empty,
                AppRect = Rect.Empty,
            };

            FluidMoveBehaviorBase.TagDictionary.Add(element, tagData);
            Assert.IsFalse(element.IsLoaded, "Element should not be loaded (not in visual tree).");

            PurgeDeadTagEntries();

            Assert.IsFalse(FluidMoveBehaviorBase.TagDictionary.ContainsKey(element),
                "Entry with unloaded element key should be removed after purge.");
        }

        [TestMethod]
        public void TagDictionary_DoesNotPurgeAliveEntries()
        {
            // Entry with a live child and a non-element key should survive purge.
            string key = "alive-item";
            var element = new Button();
            var tagData = new FluidMoveBehaviorBase.TagData
            {
                Child = element,
                Parent = null,
                ParentRect = Rect.Empty,
                AppRect = Rect.Empty,
            };

            FluidMoveBehaviorBase.TagDictionary.Add(key, tagData);

            PurgeDeadTagEntries();

            Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(key),
                "Entry with alive child and non-element key should not be purged.");

            GC.KeepAlive(element);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddEphemeralTagDataEntry(string key)
        {
            var element = new Button();
            var tagData = new FluidMoveBehaviorBase.TagData
            {
                Child = element,
                Parent = null,
                ParentRect = Rect.Empty,
                AppRect = Rect.Empty,
            };
            FluidMoveBehaviorBase.TagDictionary.Add(key, tagData);
        }

        /// <summary>
        /// Simulates the purge logic from the LayoutUpdated handler.
        /// </summary>
        private static void PurgeDeadTagEntries()
        {
            List<object> deadTags = null;
            foreach (KeyValuePair<object, FluidMoveBehaviorBase.TagData> pair in FluidMoveBehaviorBase.TagDictionary)
            {
                bool isDead = !pair.Value.IsAlive ||
                              (pair.Key is FrameworkElement fe && !fe.IsLoaded);
                if (isDead)
                {
                    if (deadTags == null) deadTags = new List<object>();
                    deadTags.Add(pair.Key);
                }
            }
            if (deadTags != null)
            {
                foreach (object tag in deadTags)
                    FluidMoveBehaviorBase.TagDictionary.Remove(tag);
            }
        }

        #endregion

        #region TransitionStoryboardDictionary Purge Tests

        [TestMethod]
        public void PurgeDeadStoryboards_RemovesEntryForUnloadedElementKey()
        {
            // An unloaded element key should be purged from the storyboard dictionary.
            var element = new Button(); // not in visual tree, IsLoaded == false
            var storyboard = new Storyboard();

            FluidMoveBehavior.InjectStoryboardEntry(element, storyboard);
            Assert.IsTrue(FluidMoveBehavior.StoryboardDictionaryContainsKey(element));

            FluidMoveBehavior.PurgeDeadStoryboards();

            Assert.IsFalse(FluidMoveBehavior.StoryboardDictionaryContainsKey(element),
                "Storyboard entry with unloaded element key should be removed after purge.");
        }

        [TestMethod]
        public void PurgeDeadStoryboards_RemovesEntryForDataContextKeyWithDeadChild()
        {
            // DataContext-type key: the key is a data object, not a FrameworkElement.
            // The storyboard should be purged when the TagDictionary entry's child is dead.
            string dataContextKey = "dc-item";
            var storyboard = new Storyboard();

            FluidMoveBehavior.InjectStoryboardEntry(dataContextKey, storyboard);
            AddEphemeralTagDataEntry(dataContextKey);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            FluidMoveBehavior.PurgeDeadStoryboards();

            Assert.IsFalse(FluidMoveBehavior.StoryboardDictionaryContainsKey(dataContextKey),
                "Storyboard entry with dead child in TagDictionary should be removed after purge.");
        }

        [TestMethod]
        public void PurgeDeadStoryboards_RemovesOrphanedDataContextKey()
        {
            // A storyboard entry whose key has no corresponding TagDictionary entry
            // should be removed as an orphan.
            string orphanKey = "orphan-dc";
            var storyboard = new Storyboard();

            FluidMoveBehavior.InjectStoryboardEntry(orphanKey, storyboard);

            // No TagDictionary entry for this key.
            FluidMoveBehavior.PurgeDeadStoryboards();

            Assert.IsFalse(FluidMoveBehavior.StoryboardDictionaryContainsKey(orphanKey),
                "Storyboard entry with no corresponding TagDictionary entry should be removed.");
        }

        #endregion

        #region Retention Tests (end-to-end with real visual tree)

        /// <summary>
        /// Pumps the WPF dispatcher so that layout, data binding, and Loaded/Unloaded events
        /// are processed. Calls DispatcherHelper.ClearFrames which pushes a frame until SystemIdle.
        /// </summary>
        private static void PumpDispatcher()
        {
            DispatcherHelper.ClearFrames(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        }

        [TestMethod]
        public void Retention_ElementTypeEntries_RemovedAfterChildRemovedFromPanel()
        {
            FluidMoveBehaviorBase.ResetPurgeThrottle();

            var window = new Window { Width = 200, Height = 200 };
            var panel = new StackPanel();
            var behavior = new FluidMoveBehavior
            {
                AppliesTo = FluidMoveScope.Children,
                Duration = new Duration(TimeSpan.FromMilliseconds(0))
            };
            Interaction.GetBehaviors(panel).Add(behavior);
            window.Content = panel;

            var child1 = new Button { Width = 50, Height = 50 };
            var child2 = new Button { Width = 50, Height = 50 };
            panel.Children.Add(child1);
            panel.Children.Add(child2);

            try
            {
                window.Show();
                PumpDispatcher();

                // Entries should exist for both children (element-type keys).
                Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child1),
                    "child1 should be tracked after layout.");
                Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child2),
                    "child2 should be tracked after layout.");

                // Remove child1 from the panel.
                panel.Children.Remove(child1);

                // First pump processes: layout pass + LayoutUpdated (Render priority),
                // then Unloaded fires (Loaded priority, lower) setting IsLoaded = false.
                // The purge runs during the first LayoutUpdated but misses child1 because
                // IsLoaded is still true at Render priority. After this pump, IsLoaded is false.
                PumpDispatcher();

                // Reset throttle and force another layout pass so the purge runs again,
                // this time seeing IsLoaded == false.
                FluidMoveBehaviorBase.ResetPurgeThrottle();
                panel.InvalidateArrange();
                PumpDispatcher();

                Assert.IsFalse(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child1),
                    "Removed child's entry should be purged from TagDictionary.");
                Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child2),
                    "Remaining child's entry should survive.");
            }
            finally
            {
                window.Close();
            }
        }

        [TestMethod]
        public void Retention_ElementTypeEntries_SurviveReparenting()
        {
            FluidMoveBehaviorBase.ResetPurgeThrottle();

            var window = new Window { Width = 300, Height = 200 };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var panelA = new StackPanel();
            var panelB = new StackPanel();
            Grid.SetColumn(panelA, 0);
            Grid.SetColumn(panelB, 1);
            grid.Children.Add(panelA);
            grid.Children.Add(panelB);

            var behaviorA = new FluidMoveBehavior
            {
                AppliesTo = FluidMoveScope.Children,
                Duration = new Duration(TimeSpan.FromMilliseconds(0))
            };
            var behaviorB = new FluidMoveBehavior
            {
                AppliesTo = FluidMoveScope.Children,
                Duration = new Duration(TimeSpan.FromMilliseconds(0))
            };
            Interaction.GetBehaviors(panelA).Add(behaviorA);
            Interaction.GetBehaviors(panelB).Add(behaviorB);

            window.Content = grid;

            var child = new Button { Width = 50, Height = 50 };
            panelA.Children.Add(child);

            try
            {
                window.Show();
                PumpDispatcher();

                Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child),
                    "child should be tracked in panelA.");

                // Reparent: move child from panelA to panelB.
                panelA.Children.Remove(child);
                panelB.Children.Add(child);

                PumpDispatcher();
                FluidMoveBehaviorBase.ResetPurgeThrottle();
                panelB.InvalidateArrange();
                PumpDispatcher();

                // The entry should survive reparenting — the child is loaded in panelB.
                Assert.IsTrue(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child),
                    "child's entry should survive reparenting (still loaded in panelB).");
            }
            finally
            {
                window.Close();
            }
        }

        [TestMethod]
        public void Retention_AllChildrenRemoved_EntriesEventuallyPurged()
        {
            FluidMoveBehaviorBase.ResetPurgeThrottle();

            var window = new Window { Width = 200, Height = 200 };
            var panel = new StackPanel();
            var behavior = new FluidMoveBehavior
            {
                AppliesTo = FluidMoveScope.Children,
                Duration = new Duration(TimeSpan.FromMilliseconds(0))
            };
            Interaction.GetBehaviors(panel).Add(behavior);
            window.Content = panel;

            var child1 = new Button { Width = 50, Height = 50 };
            var child2 = new Button { Width = 50, Height = 50 };
            panel.Children.Add(child1);
            panel.Children.Add(child2);

            try
            {
                window.Show();
                PumpDispatcher();

                int initialCount = FluidMoveBehaviorBase.TagDictionary.Count;
                Assert.IsTrue(initialCount >= 2, "Should have at least 2 entries.");

                // Remove ALL children.
                panel.Children.Clear();

                PumpDispatcher(); // processes layout + Unloaded
                FluidMoveBehaviorBase.ResetPurgeThrottle();
                panel.InvalidateArrange();
                PumpDispatcher(); // purge now sees IsLoaded == false

                Assert.IsFalse(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child1),
                    "child1 entry should be purged after removal.");
                Assert.IsFalse(FluidMoveBehaviorBase.TagDictionary.ContainsKey(child2),
                    "child2 entry should be purged after removal.");
            }
            finally
            {
                window.Close();
            }
        }

        #endregion
    }
}
