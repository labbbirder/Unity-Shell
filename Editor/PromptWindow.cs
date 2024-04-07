using System;
using UnityEditor;
using UnityEngine;

namespace com.bbbirder.unityeditor
{
    internal class PromptWindow : EditorWindow
    {
        public Action<string> onSubmit;
        public string prompt;
        string input;
        public static void Show(string prompt, Action<string> onSubmit)
        {
            var window = GetWindow<PromptWindow>();
            window.onSubmit = onSubmit;
            window.prompt = prompt;
            window.minSize = new Vector2(200, 42);
            window.maxSize = new Vector2(800, 42);
            window.ShowModal();
        }
        
        void OnGUI()
        {
            if(input is null) GUI.FocusControl(nameof(prompt));
            EditorGUILayout.PrefixLabel(prompt);
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(nameof(prompt));
            input = EditorGUILayout.TextField(input);
            var e = Event.current;
            if (GUILayout.Button("submit", GUILayout.Width(60)) || (e.isKey && e.keyCode == KeyCode.Return))
            {
                onSubmit?.Invoke(input);
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}