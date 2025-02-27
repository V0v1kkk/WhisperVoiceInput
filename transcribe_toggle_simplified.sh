#!/bin/bash

MESSAGE="transcribe_toggle"
PIPE_PATH="/tmp/WhisperVoiceInput/pipe"

echo "$MESSAGE" | socat - UNIX-CONNECT:$PIPE_PATH