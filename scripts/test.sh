#!/usr/bin/env bash
# Run all tests across all sample apps.
# Usage: ./scripts/test.sh
set -euo pipefail

cd "$(dirname "$0")/.."
failed=0

echo "=== .NET tests ==="
(cd dotnet && dotnet test WorkIQSamples.sln --nologo) || failed=1

echo ""
echo "=== Rust tests ==="
(cd rust/a2a && cargo test) || failed=1

echo ""
echo "=== Swift tests ==="
if command -v xcodebuild &>/dev/null; then
    (cd swift/a2a && xcodebuild test \
        -scheme "A2A Chat" \
        -destination "platform=iOS Simulator,name=iPhone 16" \
        -quiet 2>&1 | tail -20) || failed=1
else
    echo "  Skipped (xcodebuild not found)"
fi

echo ""
if [ "$failed" -ne 0 ]; then
    echo "FAILED: some test suites had failures"
    exit 1
else
    echo "All tests passed."
fi
