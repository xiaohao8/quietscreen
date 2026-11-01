# Third-party notices

Quiet · 屏息 includes or links against the following components.

## nomi-san/parsec-vdd

- Project: https://github.com/nomi-san/parsec-vdd
- License: MIT (Copyright (c) 2023 Nguyen Duy)
- Use: Windows virtual-display control IOCTLs and the official Parsec VDD
  driver installer are redistributed/used as described in the project.
- The full MIT text is in `LICENSE-parsec-vdd.txt` (also shipped inside the
  MSIX package).

## Parsec Virtual Display Driver

- Driver and installer: Parsec Cloud Inc.
- The installer is bundled unmodified as `parsec-vdd-0.45.0.0.exe` and is run
  only after an explicit user action and UAC confirmation.
- The driver itself is a separate kernel component and is not part of this
  application's MIT-licensed source code.

## blankrun

- Project: https://github.com/hongfaqiu/blankrun
- License: MIT
- Use: input-interception design reference (not all concepts are implemented
  in this release).

## .NET / Windows App SDK / WPF

- Copyright (c) .NET Foundation and Contributors.
- License: MIT. Distributed as part of the self-contained .NET runtime in the
  MSIX package.
