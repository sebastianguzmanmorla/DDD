# Load variables from .env if it exists
ifneq ("$(wildcard .env)","")
    include .env
endif

# Variables
PROJECT_PATH=src/SebastianGuzmanMorla.DDD/SebastianGuzmanMorla.DDD.csproj
INFRASTRUCTURE_PROJECT_PATH=src/SebastianGuzmanMorla.DDD.Infrastructure/SebastianGuzmanMorla.DDD.Infrastructure.csproj
DOMAIN_PROJECT_PATH=src/SebastianGuzmanMorla.DDD.Domain/SebastianGuzmanMorla.DDD.Domain.csproj
PACK_OUTPUT=artifacts
NUGET_SOURCE=https://api.nuget.org/v3/index.json

# Operating-system-specific commands
ifeq ($(OS),Windows_NT)
    RM_DIR = if exist "$(subst /,\,$(PACK_OUTPUT))" rmdir /s /q "$(subst /,\,$(PACK_OUTPUT))"
else
    RM_DIR = rm -rf $(PACK_OUTPUT)
endif

.PHONY: clean pack push check-env test test-unit test-integration test-package

check-env:
ifndef API_KEY
	$(error API_KEY not found. Make sure your .env file contains API_KEY=xxx)
endif

clean:
	@echo "Cleaning binaries..."
	@-$(RM_DIR)
	@dotnet clean $(PROJECT_PATH) -c Release
	@dotnet clean $(INFRASTRUCTURE_PROJECT_PATH) -c Release
	@dotnet clean $(DOMAIN_PROJECT_PATH) -c Release

build:
	@echo "Building projects..."
	@dotnet build $(DOMAIN_PROJECT_PATH) -c Release
	@dotnet build $(PROJECT_PATH) -c Release
	@dotnet build $(INFRASTRUCTURE_PROJECT_PATH) -c Release

test:
	@dotnet test SebastianGuzmanMorla.DDD.slnx -c Release

test-unit:
	@dotnet test SebastianGuzmanMorla.DDD.slnx -c Release --filter 'Category!=Integration'

test-integration:
	@dotnet test tests/Consumer/Consumer.Integration.Tests -c Release

test-package:
	@python3 tests/packaged-consumer-smoke.py

pack: clean build
	@echo "Packing projects..."
	@dotnet pack $(DOMAIN_PROJECT_PATH) -c Release -o $(PACK_OUTPUT)
	@dotnet pack $(PROJECT_PATH) -c Release -o $(PACK_OUTPUT)
	@dotnet pack $(INFRASTRUCTURE_PROJECT_PATH) -c Release -o $(PACK_OUTPUT)

push: check-env pack
	@echo "Publishing to NuGet..."
	@dotnet nuget push $(PACK_OUTPUT)/*.nupkg \
		--api-key $(API_KEY) \
		--source $(NUGET_SOURCE) \
		--skip-duplicate
