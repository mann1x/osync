![GitHub all releases](https://img.shields.io/github/downloads/mann1x/osync/total)
![GitHub release (latest by date)](https://img.shields.io/github/v/release/mann1x/osync)
![GitHub contributors](https://img.shields.io/github/contributors/mann1x/osync)
![GitHub Repo stars](https://img.shields.io/github/stars/mann1x/osync?style=social)

<div align="center">
  <h1>osync</h1>
  <br />
  <br />
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=bug&template=01_BUG_REPORT.md&title=bug%3A+">Report a Bug</a>
  ·
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=enhancement&template=02_FEATURE_REQUEST.md&title=feat%3A+">Request a Feature</a>
  .
  <a href="https://github.com/mann1x/osync/issues/new?assignees=&labels=question&template=04_SUPPORT_QUESTION.md&title=support%3A+">Ask a Question</a>
</div>

<div align="center">
<br />

[![Project license](https://img.shields.io/github/license/mann1x/osync.svg?style=flat-square)](LICENSE)

[![Pull Requests welcome](https://img.shields.io/badge/PRs-welcome-ff69b4.svg?style=flat-square)](https://github.com/mann1x/osync/issues?q=is%3Aissue+is%3Aopen+label%3A%22help+wanted%22)
[![code with love by mann1x](https://img.shields.io/badge/%3C%2F%3E%20with%20%E2%99%A5%20by-mann1x-ff1414.svg?style=flat-square)](https://github.com/mann1x)

</div>



---

## About

> **[Copy a local ollama model to a remote server]**

> Skips already transferred images

> Uploads at high speed with a progress bar

> No more multiple downloads of the same model on different ollama hosts

> Ideal for servers isolated from internet

### Built With

> **[C# .NET 8]**

## Getting Started

### Prerequisites

> **[Windows/Linux/MacOS]**

> **[Arm64/x64/Mac]**

### Installation

> **[Download latest binary release]**

> **[Build from sources]**

> Clone the repo

> Compile with Visual Studio 2022

## Usage

> Simple: `osync cp modelname http://192.168.100.100:11434`

> available action commands:

- `copy` (available as alias `cp`)

    - `source` (local ollama)
    
    - `destination` (remote ollama)

> Command line arguments:

- `-h` for help

> Execute without arguments to get local models TabCompletion!

## Known Issues

> None

## Changelog

v1.0.6
- Changed syntax to support multiple actions: now copying a model needs `copy` (alias `cp`)
- Fixed upload progress bar updates
- Handles automatically `latest` tag if none specified

v1.0.5
- Added local models TabCompletion with interactive prompt if called without arguments
- Fixed 100 seconds timeout
- Added arguments exception handling

v1.0.4
- Check remote ollama version and display its version
- Fixed streaming output from create model

v1.0.3
- Added -bt switch to throttle the bandwidth in B, KB, MB, GB per second, eg. for 75MB/s use `-bt 75MB`

v1.0.2
- Fixed build with single portable file for Linux/MacOS

v1.0.1
- Fixed bug with stdErr redirect
- Remove Linux and MacOS colored output

v1.0.0
- Initial release

## Roadmap

See the [open issues](https://github.com/mann1x/osync/issues) for a list of proposed features (and known issues).

- [Top Feature Requests](https://github.com/mann1x/osync/issues?q=label%3Aenhancement+is%3Aopen+sort%3Areactions-%2B1-desc) (Add your votes using the 👍 reaction)
- [Top Bugs](https://github.com/mann1x/osync/issues?q=is%3Aissue+is%3Aopen+label%3Abug+sort%3Areactions-%2B1-desc) (Add your votes using the 👍 reaction)
- [Newest Bugs](https://github.com/mann1x/osync/issues?q=is%3Aopen+is%3Aissue+label%3Abug)

## Support

- [GitHub issues](https://github.com/mann1x/osync/issues/new?assignees=&labels=question&template=04_SUPPORT_QUESTION.md&title=support%3A+)


## License

This project is licensed under the **MIT license**.

See [LICENSE](LICENSE) for more information.
