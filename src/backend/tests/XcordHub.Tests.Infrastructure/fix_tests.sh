#!/bin/bash

# Fix Email field (string to byte[])
for file in *.cs; do
    if [[ "$file" == "TestHelpers.cs" || "$file" == "Fixtures"* ]]; then
        continue
    fi
    
    # Replace Email = "..." with encoded bytes using TestHelpers
    sed -i 's/var owner = new HubUser/var owner = TestHelpers.CreateTestUser(/' "$file"
    sed -i '/Email = .*@test.com/d' "$file"
    sed -i '/Username = /d' "$file"
    sed -i '/CreatedAt = DateTimeOffset.UtcNow/d' "$file"
    sed -i 's/new HubUser$//' "$file"
    sed -i 's/{$//' "$file"
    sed -i 's/Id = \([0-9]*\),$/\1, "user\1"),/' "$file"
done
