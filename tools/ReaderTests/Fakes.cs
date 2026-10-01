// Test boundary only: managed stand-ins for members used by the full ScreenReader.
// These do NOT emulate native liveness, CanvasGroup visibility, IL2CPP or hooks.
// All scheduling, snapshots, context reads and utterance composition are production.
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Il2CppInterop.Runtime
{
    public static class Il2CppType
    {
        public static Type Of<T>() => typeof(T);
    }
}

namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppReferenceArray<T>
    {
        private readonly T[] _items;
        public Il2CppReferenceArray(IEnumerable<T> items) { _items = items.ToArray(); }
        public int Length => _items.Length;
        public T this[int index] => _items[index];
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class List<T> : System.Collections.Generic.List<T> { }
}

namespace UnityEngine
{
    public class Object
    {
        private static int _nextId;
        private readonly int _id = ++_nextId;
        public string name { get; set; } = string.Empty;
        public int GetInstanceID() => _id;
        public T TryCast<T>() where T : Object => this as T;
        public static Il2CppReferenceArray<Object> FindObjectsOfType(Type type) =>
            new Il2CppReferenceArray<Object>(ReaderTests.FakeWorld.Objects.Where(type.IsInstanceOfType));
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
        public Il2CppReferenceArray<Component> GetComponentsInChildren(Type type, bool includeInactive) =>
            gameObject.GetComponentsInChildren(type, includeInactive);
    }

    public sealed class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public Transform transform { get; }
        public bool activeInHierarchy { get; set; } = true;
        // A test input, not an implementation of Unity canvas visibility.
        public bool FakeVisible { get; set; } = true;
        public GameObject(string label)
        {
            name = label;
            transform = new Transform { gameObject = this, name = label };
            Components.Add(transform);
        }
        public Il2CppReferenceArray<Component> GetComponentsInChildren(Type type, bool includeInactive) =>
            new Il2CppReferenceArray<Component>(Descendants(this, includeInactive).Where(type.IsInstanceOfType));
        private static IEnumerable<Component> Descendants(GameObject obj, bool includeInactive)
        {
            if (!includeInactive && !obj.activeInHierarchy) yield break;
            foreach (Component component in obj.Components) yield return component;
            foreach (Transform child in obj.transform.Children)
                foreach (Component component in Descendants(child.gameObject, includeInactive))
                    yield return component;
        }
    }

    public sealed class Transform : Component
    {
        internal readonly List<Transform> Children = new List<Transform>();
        public Transform parent { get; private set; }
        public Vector3 position { get; set; }
        public int GetSiblingIndex() => parent == null ? 0 : parent.Children.IndexOf(this);
        internal void AttachForTest(Transform target)
        {
            parent?.Children.Remove(this);
            parent = target;
            parent?.Children.Add(this);
        }
    }

    public struct Vector3 { public float y { get; set; } }
    public class Sprite : Object { }
}

namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component { public UnityEngine.Sprite sprite { get; set; } }
}

namespace Il2CppTMPro
{
    public class TMP_Text : UnityEngine.Component { public string text { get; set; } }
}

namespace Il2Cppecho17.EndlessBook
{
    public class EndlessBook : UnityEngine.Component
    {
        public int CurrentLeftPageNumber { get; set; }
        public int CurrentRightPageNumber { get; set; }
        public int LastPageNumber { get; set; }
    }
}

namespace Il2Cpp
{
    public class Panel : UnityEngine.Component
    {
        public bool IsFullyVisible { get; set; } = true;
        public bool IsVisible { get; set; } = true;
        public bool IsTransitioningHide { get; set; }
    }
    public enum CalibrateState { Start, Warmup, Calibrate, Finished, Result }
    public class CalibratePanel : Panel
    {
        public CalibrateState State { get; set; }
        public Il2CppTMPro.TMP_Text countdown { get; set; }
        public Il2CppTMPro.TMP_Text latency { get; set; }
        public float FakeEstimatedLatency { get; set; } = 42;
        public float EstimateLatency() => FakeEstimatedLatency;
    }
    public class AchievementsPanel : Panel { }
    public class CreditsContent : UnityEngine.Component { }
    public class BookController : UnityEngine.Component
    {
        public Il2Cppecho17.EndlessBook.EndlessBook Book { get; set; }
    }
    public class AchievementListItem : UnityEngine.Component
    {
        public BopItAchievement Achievement { get; set; }
        public string Description { get; set; }
    }
    public class BopItAchievement
    {
        public string Title { get; set; }
        public string SubGroup { get; set; }
        public string Group { get; set; }
        public bool IsUnlocked { get; set; }
    }
    public enum CombinedLeaderboardMode { MainMenu, Game }
    public class CombinedLeaderboardPanel : Panel
    {
        public CombinedLeaderboardMode Mode { get; set; }
        public DeviceContainer deviceContainer { get; set; }
        public UnityEngine.UI.Image TitleImage { get; set; }
        public Il2CppSystem.Collections.Generic.List<TitleBackgroundTable> TrackImages { get; set; }
    }
    public class TitleBackgroundTable
    {
        public UnityEngine.Sprite Image { get; set; }
        public string TrackName { get; set; }
    }
    public class GroupFilterTab : UnityEngine.Component
    {
        public bool IsActive { get; set; }
        public Il2CppTMPro.TMP_Text Text { get; set; }
    }
    public class DateFilterTab : UnityEngine.Component { public bool IsActive { get; set; } }
    public enum LeaderboardItemType { Normal, First, Me, MostRecent }
    public class LeaderboardLineItem : UnityEngine.Component
    {
        public int Rank { get; set; }
        public Il2CppTMPro.TMP_Text RankText { get; set; }
        public Il2CppTMPro.TMP_Text AliasText { get; set; }
        public Il2CppTMPro.TMP_Text ScoreText { get; set; }
        public LeaderboardItemType ItemType { get; set; }
    }
    public class Device : UnityEngine.Component { public Il2CppBopIt.DeviceType Type { get; set; } }
    public enum GameState { WaitingToStart, Paused, Playing, GameOver }
    public class StartScreenPanel : Panel { }
    public class GameUIManager : UnityEngine.Component { public StartScreenPanel startScreen { get; set; } }
    public class Player : UnityEngine.Component { public Device Device { get; set; } }
    public class GameManager : UnityEngine.Component
    {
        public GameState GameState { get; set; }
        public Player Player { get; set; }
    }
    public class App : UnityEngine.Component { public MusicTrack CurrentMusicTrack { get; set; } }
    public class MusicTrack : UnityEngine.Object { public string BackgroundSceneName { get; set; } }
    public class DeviceContainer : UnityEngine.Component { public Device CurrentDevice { get; set; } }
}

namespace Il2CppBopIt
{
    public enum DeviceType { Classic, Extreme }
}

namespace BlindIt
{
    internal static class Ui
    {
        internal static UnityEngine.GameObject FakeSelected { get; set; }
        internal static UnityEngine.GameObject Selected() => FakeSelected;
        internal static bool Alive(UnityEngine.Object obj) => obj != null;
        internal static bool Visible(UnityEngine.GameObject obj) =>
            obj != null && obj.activeInHierarchy && obj.FakeVisible;
        internal static string SafeName(UnityEngine.Object obj) => obj?.name;
        internal static string Path(UnityEngine.GameObject obj) => obj?.name;
    }
    internal static class Hotkeys
    {
        internal static bool NextDown { get; set; }
        internal static bool PreviousDown { get; set; }
        internal static bool RepeatDown { get; set; }
        internal static bool NextPressed() => NextDown;
        internal static bool PreviousPressed() => PreviousDown;
        internal static bool ReviewNextPressed() => NextDown;
        internal static bool ReviewPreviousPressed() => PreviousDown;
        internal static bool RepeatPressed() => RepeatDown;
        internal static string ReviewHelp(bool arrows) => Strings.Get(arrows ? "review.help.arrows" : "review.help");
        internal static void Reset() { NextDown = false; PreviousDown = false; RepeatDown = false; }
    }
    internal static class Log
    {
        internal static readonly List<string> Lines = new List<string>();
        internal static void Line(string category, string text) => Lines.Add(category + " | " + text);
    }
    internal static class Speech
    {
        // Intentionally records EVERY call. No deduplication, queuing or NVDA use.
        internal static readonly List<ReaderTests.Utterance> Spoken = new List<ReaderTests.Utterance>();
        internal static void Speak(string text, bool interrupt) => Spoken.Add(
            new ReaderTests.Utterance(ReaderTests.FakeWorld.Frame, ReaderTests.FakeWorld.Input, text, interrupt));
    }
}

namespace ReaderTests
{
    internal sealed record Utterance(int Frame, string Input, string Text, bool Interrupt);
    internal static class FakeWorld
    {
        internal static readonly List<UnityEngine.Object> Objects = new List<UnityEngine.Object>();
        internal static int Frame { get; set; }
        internal static string Input { get; set; } = "automatic";
        internal static T Add<T>(string name, UnityEngine.GameObject parent = null)
            where T : UnityEngine.Component, new()
        {
            var obj = new UnityEngine.GameObject(name);
            obj.transform.AttachForTest(parent?.transform);
            var component = new T { gameObject = obj, name = name };
            obj.Components.Add(component);
            Objects.Add(obj);
            Objects.Add(obj.transform);
            Objects.Add(component);
            return component;
        }
        internal static Il2CppTMPro.TMP_Text Text(string name, string text, UnityEngine.GameObject parent)
        {
            var component = Add<Il2CppTMPro.TMP_Text>(name, parent);
            component.text = text;
            return component;
        }
        internal static void Reset()
        {
            Objects.Clear();
            Frame = 0;
            Input = "automatic";
            BlindIt.Ui.FakeSelected = null;
            BlindIt.Hotkeys.Reset();
            BlindIt.Speech.Spoken.Clear();
            BlindIt.Log.Lines.Clear();
        }
    }
}
