#!/usr/bin/env python3
"""Build and run the layered consumer against local NuGet packages on both frameworks."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
LIBRARIES = ["SebastianGuzmanMorla.DDD.Domain", "SebastianGuzmanMorla.DDD", "SebastianGuzmanMorla.DDD.Infrastructure"]
CONSUMERS = ["Consumer.Contracts", "Consumer.Domain", "Consumer.Infrastructure", "Consumer.Application"]


def run(*args, cwd=ROOT, env=None):
    subprocess.run(args, cwd=cwd, env=env, check=True)


def main():
    version = "0.0.0-smoke." + uuid.uuid4().hex
    env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE="en")
    with tempfile.TemporaryDirectory(prefix="ddd-package-smoke-") as temporary:
        # Canonical paths avoid /var vs /private/var project-reference mismatches on macOS.
        directory = Path(temporary).resolve()
        feed = directory / "feed"
        feed.mkdir()
        for library in LIBRARIES:
            run("dotnet", "pack", str(ROOT / "src" / library / (library + ".csproj")),
                "-c", "Release", "-o", str(feed), "-p:Version=" + version,
                "-p:GeneratePackageOnBuild=false", env=env)

        # Use a private restore cache: the smoke packages never enter the user's cache.
        env["NUGET_PACKAGES"] = str(directory / "packages")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="local", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        ET.ElementTree(config).write(directory / "NuGet.Config", encoding="unicode")
        shutil.copyfile(ROOT / "tests/Consumer/Directory.Build.props", directory / "Directory.Build.props")

        for consumer in CONSUMERS:
            target = directory / consumer
            shutil.copytree(ROOT / "tests/Consumer" / consumer, target, ignore=shutil.ignore_patterns("bin", "obj"))
            project = target / (consumer + ".csproj")
            tree = ET.parse(project)
            for group in tree.getroot().findall("ItemGroup"):
                for reference in list(group.findall("ProjectReference")):
                    name = Path(reference.attrib["Include"].replace("\\", "/")).stem
                    if name.endswith(".Generator"):
                        # The library packages must supply their own analyzers transitively.
                        group.remove(reference)
                    elif name in LIBRARIES:
                        group.remove(reference)
                        ET.SubElement(group, "PackageReference", Include=name, Version=version)
            tree.write(project, encoding="unicode")

        smoke = directory / "Consumer.PackageSmoke"
        smoke.mkdir()
        (smoke / "Consumer.PackageSmoke.csproj").write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
  <ItemGroup><ProjectReference Include="../Consumer.Application/Consumer.Application.csproj" /></ItemGroup>
</Project>
''')
        shutil.copyfile(ROOT / "tests/PackageSmoke/Program.cs", smoke / "Program.cs")
        for framework in ("net9.0", "net10.0"):
            run("dotnet", "run", "--project", str(smoke), "-c", "Release", "-f", framework,
                cwd=directory, env=env)
        print("Packaged consumer smoke tests passed on .NET 9 and .NET 10.", flush=True)


if __name__ == "__main__":
    main()
