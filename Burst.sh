#!/bin/bash

echo "🚀 Setting up Python environment..."

# Check if the actual activate file exists. If not, wipe any broken folder and rebuild.
if [ ! -f ".venv/bin/activate" ]; then
    echo "📦 Creating virtual environment..."
    rm -rf .venv
    python3 -m venv .venv
fi

# Activate the virtual environment
source .venv/bin/activate

# Ensure httpx is installed (silently)
pip install httpx > /dev/null 2>&1

# Capture the URL passed as an argument, or default to localhost
TARGET_URL=${1:-"http://localhost:8080"}

# Run the Python script
python3 Burst.py "$TARGET_URL"