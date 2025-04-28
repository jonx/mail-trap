# mail-trap

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Made with: C#](https://img.shields.io/badge/made%20with-C%23-blue.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)

**mail-trap** is a minimal local SMTP server for development.  
It accepts emails via SMTP, saves them as `.eml` files, and displays basic information like sender, recipient, and subject in the console.

It is designed for quick testing of applications, services, or workflows (like **n8n**) that need to send emails without needing an external mail server.

---

## Features

- Listens on a configurable TCP port (default: **2525**)
- Receives SMTP commands: `HELO`, `MAIL FROM`, `RCPT TO`, `DATA`, `QUIT`
- Saves received emails as `.eml` files in the `data/` folder
- Displays sender, recipient, and subject in the console
- Command-line options for port selection and help
- No authentication, no TLS — minimal by design
- No external libraries needed (pure .NET)

---

## Running

Requirements:
- .NET 6.0 SDK or newer

First, build the project:

```bash
dotnet build
```

### During Development

To run directly from source:

```bash
dotnet run -- [options]
```

Example:

```bash
dotnet run -- --port 2526
```

### After Build (Recommended)

Find the executable in:

```
bin/Debug/net6.0/mail-trap.exe
```

Run it directly:

```bash
mail-trap [options]
```

Example:

```bash
mail-trap --port 2526
```

Display help:

```bash
mail-trap --help
```

---

## Options

| Option | Description |
|:-------|:------------|
| `-p`, `--port <number>` | Set the listening port (default: 2525) |
| `-h`, `--help` | Show help message |

---

## Important Notes for Docker Users

When running applications **inside Docker** (such as **n8n**),  
remember that `localhost` inside the container **is not** your Windows or host machine.

- Use `host.docker.internal` as the SMTP server address.
- Example SMTP configuration inside Docker:

  ```
  host: host.docker.internal
  port: 2525
  ```

Otherwise, you will encounter connection errors such as `ECONNREFUSED`.

---

## Example Setup for n8n

SMTP Email node settings:

| Setting         | Value                       |
|-----------------|------------------------------|
| Host            | `host.docker.internal`        |
| Port            | `2525` (or your custom port)  |
| Secure          | No                            |
| Authentication  | None                          |

---

## Limitations

- No TLS or SSL (plaintext SMTP only)
- No authentication support
- Accepts one email per connection
- Minimal error handling

**mail-trap** is intended for **local development only**, not for production use.

---

## License

MIT License.  
Copyright (c) 2025 John Knipper.

See [LICENSE](LICENSE) for more information.

---

# Short About

> A minimal local SMTP server for development.  
> Receives and saves emails as `.eml` files.

---

