# ADR 0005: Settings and secret storage

Status: Accepted

Store non-secret settings as versioned JSON written atomically under platform-conventional configuration paths. Preserve or explicitly migrate unknown versions. Store API keys via DPAPI, Keychain, or Secret Service adapters; when unavailable, retain secrets for the process only and explain that persistence is disabled. Never downgrade to plaintext.
