using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class SOFieldWriteRequestFileBridge : MonoBehaviour
    {
        [SerializeField] private SOFieldWriteRequestSO request;
        [SerializeField] private string relativeCommandPath = "DataCapture/so_write_requests.jsonl";
        [SerializeField] private float pollIntervalSeconds = 0.5f;
        [SerializeField] private bool createDirectoryOnStart = true;

        private string commandPath;
        private long readOffset;
        private float nextPollTime;
        private readonly Queue<SOFieldWriteCommand> pendingCommands = new Queue<SOFieldWriteCommand>();

        private void Start()
        {
            commandPath = Path.Combine(Application.persistentDataPath, relativeCommandPath);

            if (createDirectoryOnStart)
            {
                string directory = Path.GetDirectoryName(commandPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        private void Update()
        {
            if (request == null)
            {
                return;
            }

            if (Time.unscaledTime >= nextPollTime)
            {
                nextPollTime = Time.unscaledTime + Mathf.Max(0.05f, pollIntervalSeconds);
                PollCommandFile();
            }

            DispatchNextPendingCommand();
        }

        [ContextMenu("Log Command File Path")]
        public void LogCommandFilePath()
        {
            if (string.IsNullOrEmpty(commandPath))
            {
                commandPath = Path.Combine(Application.persistentDataPath, relativeCommandPath);
            }

            Debug.Log("SO field write command file: " + commandPath, this);
        }

        private void PollCommandFile()
        {
            if (string.IsNullOrEmpty(commandPath) || !File.Exists(commandPath))
            {
                return;
            }

            using (FileStream stream = new FileStream(commandPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length < readOffset)
                {
                    readOffset = 0;
                }

                stream.Seek(readOffset, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        readOffset += System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        EnqueueJsonLine(line);
                    }
                }
            }
        }

        private void EnqueueJsonLine(string line)
        {
            SOFieldWriteCommand command;
            try
            {
                command = JsonUtility.FromJson<SOFieldWriteCommand>(line);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Invalid SO write request JSON: " + exception.Message + " line=" + line, this);
                return;
            }

            if (command == null || string.IsNullOrWhiteSpace(command.targetName) || string.IsNullOrWhiteSpace(command.fieldPath))
            {
                Debug.LogWarning("SO write request JSON must include targetName and fieldPath. line=" + line, this);
                return;
            }

            pendingCommands.Enqueue(command);
        }

        private void DispatchNextPendingCommand()
        {
            if (request == null || request.requested || pendingCommands.Count == 0)
            {
                return;
            }

            SOFieldWriteCommand command = pendingCommands.Dequeue();
            request.target = null;
            request.targetName = command.targetName;
            request.fieldPath = command.fieldPath;
            request.valueType = command.valueType;
            request.stringValue = command.stringValue ?? string.Empty;
            request.boolValue = command.boolValue;
            request.intValue = command.intValue;
            request.longValue = command.longValue;
            request.floatValue = command.floatValue;
            request.vector2Value = command.vector2Value;
            request.vector3Value = command.vector3Value;
            request.Request(string.IsNullOrWhiteSpace(command.source) ? "SOFieldWriteRequestFileBridge" : command.source);
        }

        [Serializable]
        private class SOFieldWriteCommand
        {
            public string source;
            public string targetName;
            public string fieldPath;
            public SOFieldWriteValueType valueType;
            public string stringValue;
            public bool boolValue;
            public int intValue;
            public long longValue;
            public float floatValue;
            public Vector2 vector2Value;
            public Vector3 vector3Value;
        }
    }
}
