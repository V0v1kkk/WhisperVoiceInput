#!/bin/bash

MESSAGE="transcribe_toggle"
PIPE_PATH="/tmp/WhisperVoiceInput/pipe"

# Check if socat is installed
if ! command -v socat &> /dev/null; then
    echo "Error: socat is not installed. Please install it with your package manager."
    echo "For example: sudo apt install socat"
    exit 1
fi

# Check if the socket exists
if [ ! -S "$PIPE_PATH" ]; then
    echo "Error: Socket $PIPE_PATH does not exist."
    echo "Make sure WhisperVoiceInput is running."
    exit 1
fi

echo "Sending '$MESSAGE' command to WhisperVoiceInput..."
echo "$MESSAGE" | socat - UNIX-CONNECT:$PIPE_PATH
echo "Command sent."