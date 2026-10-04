#!/usr/bin/env bash
set -e

BASE_URL=${1:-"http://localhost:5053"}
echo "Setting up Python environment..."

# Create virtual env if it doesn't exist
if [ ! -d ".venv" ]; then
    python3 -m venv .venv
fi

source .venv/bin/activate
pip install -q httpx

echo "Running burst script against $BASE_URL..."
python Burst.py "$BASE_URL"