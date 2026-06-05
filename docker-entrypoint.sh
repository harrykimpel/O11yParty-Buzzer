#!/bin/sh
set -e

# Pick the New Relic CoreCLR profiler that matches the container's CPU architecture.
# The arm64 profiler lives in a linux-arm64 subdirectory; x64 is at the agent root.
case "$(uname -m)" in
  x86_64)  export CORECLR_PROFILER_PATH=/app/newrelic/libNewRelicProfiler.so ;;
  aarch64) export CORECLR_PROFILER_PATH=/app/newrelic/linux-arm64/libNewRelicProfiler.so ;;
  *)       echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

exec dotnet O11yPartyBuzzer.dll
