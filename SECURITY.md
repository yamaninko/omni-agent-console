# Security Policy

## Supported versions

Security fixes are applied on the default branch (`main`) of this repository.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security-sensitive reports.

Prefer one of:

1. GitHub **Security Advisories** for this repository (if enabled), or  
2. Email the maintainers via the address listed on the GitHub org/user profile, with subject  
   `[SECURITY] omni-agent-console …`

Include:

- Affected version / commit if known  
- Reproduction steps  
- Impact (data exposure, RCE, auth bypass, etc.)  
- Whether a fix is already known  

We aim to acknowledge within **7 days** and to publish a fix or mitigation as soon as practical.

## Hardening notes (operators)

- Set `CONSOLE_API_KEY` in any shared or internet-facing deployment.  
- Prefer `SHARED_LAB=true` only with a non-empty console key (startup fails otherwise).  
- Keep infra ports on loopback (`INFRA_BIND_ADDRESS=127.0.0.1`) unless you terminate TLS elsewhere.  
- Never commit real API keys; use Settings / Vault / `OMNIAGENT_API_KEY` env.  
- Workspace paths are guarded server-side; treat generated code as untrusted until reviewed.
