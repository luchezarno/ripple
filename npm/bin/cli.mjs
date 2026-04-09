#!/usr/bin/env node

import { spawn } from "child_process";
import { fileURLToPath } from "url";
import { dirname, join } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const exe = join(__dirname, "..", "dist", "splash.exe");

const child = spawn(exe, process.argv.slice(2), {
  stdio: "inherit",
  windowsHide: true,
});

child.on("exit", (code) => process.exit(code ?? 1));
child.on("error", (err) => {
  if (err.code === "ENOENT") {
    console.error(
      "splash binary not found. This package requires Windows and .NET 9 Desktop Runtime.\n" +
        "Download: https://dotnet.microsoft.com/download/dotnet/9.0"
    );
  } else {
    console.error(err.message);
  }
  process.exit(1);
});
