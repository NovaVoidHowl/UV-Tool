SHELL := /bin/bash
.DEFAULT_GOAL := help

PACKAGE_JSON := $(CURDIR)/package.json
BUILD_SCRIPT := $(CURDIR)/scripts/build.sh
BUILD_CONFIG ?= $(CURDIR)/build.config.mk

# Optional, developer-local paths. Command-line variables still take precedence.
-include $(BUILD_CONFIG)

TEST_PROJECT ?= $(CURDIR)/ValidationProject~
TEST_RESULTS ?= $(TEST_PROJECT)/Logs/uv-tile-scanner-test-results.xml
TEST_LOG ?= $(TEST_PROJECT)/Logs/uv-tile-scanner-test-run.log
UNITY_DATA_DIR ?= $(UNITY_EDITOR_DIR)
UNITY_EXECUTABLE ?=

PROJECTS := \
	Source~/Nvh.UvTileScanner.Core.Editor.csproj \
	Source~/Nvh.UvTileScanner.Poiyomi.Editor.csproj \
	Source~/Nvh.UvTileScanner.Editor.csproj

.PHONY: help info init-config version set-version build rebuild clean sync-tests test test-only verify list-dlls require-unity-data require-test-project

help: ## Show available targets with a one-line description.
	@awk 'BEGIN {FS = ":.*## "; printf "UV Tile Scanner development commands\n\n"} /^[a-zA-Z0-9_.-]+:.*## / {printf "  %-16s %s\n", $$1, $$2}' $(MAKEFILE_LIST)
	@printf '\nRun make init-config UNITY_DATA_DIR=/path/to/Unity/Editor/Data for persistent local paths.\n'
	@printf 'Alternatively, set UNITY_DATA_DIR or UNITY_EDITOR_DIR in the environment or command line.\n'
	@printf 'For tests, UNITY_EXECUTABLE may override the executable derived from UNITY_DATA_DIR.\n'

info: help ## Show the command summary plus package, Unity, test, and output configuration.
	@printf '\nResolved configuration\n\n'
	@if [[ -f "$(BUILD_CONFIG)" ]]; then \
		printf 'Build config:     %s (loaded)\n' "$(BUILD_CONFIG)"; \
	else \
		printf 'Build config:     %s (not found; optional)\n' "$(BUILD_CONFIG)"; \
	fi
	@printf 'Package:          %s\n' "$$(awk -F'"' '/^[[:space:]]*"name"[[:space:]]*:/ {print $$4; exit}' "$(PACKAGE_JSON)")"
	@printf 'Package version:  %s\n' "$$(awk -F'"' '/^[[:space:]]*"version"[[:space:]]*:/ {print $$4; exit}' "$(PACKAGE_JSON)")"
	@printf 'Unity Editor dir: %s\n' "$(if $(strip $(UNITY_EDITOR_DIR)),$(UNITY_EDITOR_DIR),<not set>)"
	@printf 'Unity Data:       %s\n' "$(if $(strip $(UNITY_DATA_DIR)),$(UNITY_DATA_DIR),<not set>)"
	@printf 'Unity executable: %s\n' "$(if $(strip $(UNITY_EXECUTABLE)),$(UNITY_EXECUTABLE),$(if $(strip $(UNITY_DATA_DIR)),<derived from Unity Data>,<not set>))"
	@printf 'Test project:     %s\n' "$(TEST_PROJECT)"
	@printf 'Test results:     %s\n' "$(TEST_RESULTS)"
	@printf 'Test log:         %s\n' "$(TEST_LOG)"

init-config: ## Create the ignored local build config from supplied Unity paths.
	@set -euo pipefail; \
	if [[ -e "$(BUILD_CONFIG)" ]]; then \
		echo "Build config already exists; it was not overwritten: $(BUILD_CONFIG)" >&2; \
		exit 2; \
	fi; \
	if [[ -z "$(UNITY_DATA_DIR)" && -z "$(UNITY_EDITOR_DIR)" ]]; then \
		echo 'Supply UNITY_DATA_DIR or UNITY_EDITOR_DIR when creating the config.' >&2; \
		echo 'Example: make init-config UNITY_DATA_DIR=/path/to/Unity/Editor/Data' >&2; \
		exit 2; \
	fi; \
	mkdir -p "$(dir $(BUILD_CONFIG))"; \
	printf '%s\n' \
		'# Local Unity paths for this checkout. This file is intentionally ignored by Git.' \
		'UNITY_DATA_DIR := $(UNITY_DATA_DIR)' \
		'UNITY_EDITOR_DIR := $(UNITY_EDITOR_DIR)' \
		'UNITY_EXECUTABLE := $(UNITY_EXECUTABLE)' \
		> "$(BUILD_CONFIG)"; \
	echo "Created local build config: $(BUILD_CONFIG)"

version: ## Print the package version from package.json.
	@awk -F'"' '/^[[:space:]]*"version"[[:space:]]*:/ {print $$4; found=1; exit} END {if (!found) exit 2}' "$(PACKAGE_JSON)"

set-version: ## Update package.json; usage: make set-version VERSION=1.2.3.
	@set -euo pipefail; \
	if [[ -z "$(VERSION)" ]]; then \
		echo 'VERSION is required. Example: make set-version VERSION=1.3.3' >&2; \
		exit 2; \
	fi; \
	if [[ ! "$(VERSION)" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?([+][0-9A-Za-z.-]+)?$$ ]]; then \
		echo 'VERSION must be a semantic version such as 1.3.3, 1.4.0-beta.1, or 1.4.0+build.2.' >&2; \
		exit 2; \
	fi; \
	current_version="$$(awk -F'"' '/^[[:space:]]*"version"[[:space:]]*:/ {print $$4; exit}' "$(PACKAGE_JSON)")"; \
	if [[ "$$current_version" == "$(VERSION)" ]]; then \
		echo "Package version is already $(VERSION)."; \
		exit 0; \
	fi; \
	temp_file="$$(mktemp "$(CURDIR)/package.json.XXXXXX")"; \
	trap 'rm -f "$$temp_file"' EXIT; \
	awk -v next_version="$(VERSION)" 'BEGIN {updated=0} !updated && /^[[:space:]]*"version"[[:space:]]*:/ {sub(/"[^"]+"[[:space:]]*,[[:space:]]*$$/, "\"" next_version "\","); updated=1} {print} END {if (!updated) exit 2}' "$(PACKAGE_JSON)" > "$$temp_file"; \
	chmod --reference="$(PACKAGE_JSON)" "$$temp_file"; \
	mv "$$temp_file" "$(PACKAGE_JSON)"; \
	trap - EXIT; \
	echo "Updated package version: $$current_version -> $(VERSION)"

build: require-unity-data ## Build all three Editor DLLs into Editor/.
	@"$(BUILD_SCRIPT)" "$(UNITY_DATA_DIR)"

rebuild: clean ## Clean all projects and then rebuild every DLL.
	@$(MAKE) --no-print-directory build UNITY_DATA_DIR="$(UNITY_DATA_DIR)"

clean: require-unity-data ## Run dotnet clean for each compiled DLL project.
	@set -euo pipefail; \
	for project in $(PROJECTS); do \
		dotnet clean "$$project" --configuration Release -p:UnityEditorDir="$(UNITY_DATA_DIR)"; \
	done

sync-tests: require-test-project ## Copy Tests~ sources and shader fixtures into the validation project.
	@mkdir -p "$(TEST_PROJECT)/Assets/EditorTests/Fixtures"
	@cp "Tests~/UvTileScannerTests.cs" "$(TEST_PROJECT)/Assets/EditorTests/UvTileScannerTests.cs"
	@cp "Tests~/Nvh.UvTileScanner.Tests.asmdef" "$(TEST_PROJECT)/Assets/EditorTests/Nvh.UvTileScanner.Tests.asmdef"
	@cp Tests~/Fixtures/*.shader "$(TEST_PROJECT)/Assets/EditorTests/Fixtures/"

test: build ## Build the DLLs and run all Edit Mode tests in the validation project.
	@$(MAKE) --no-print-directory test-only UNITY_DATA_DIR="$(UNITY_DATA_DIR)" UNITY_EXECUTABLE="$(UNITY_EXECUTABLE)" TEST_PROJECT="$(TEST_PROJECT)" TEST_RESULTS="$(TEST_RESULTS)" TEST_LOG="$(TEST_LOG)"

test-only: sync-tests ## Run Edit Mode tests without rebuilding the DLLs.
	@set -euo pipefail; \
	mkdir -p "$(dir $(TEST_RESULTS))" "$(dir $(TEST_LOG))"; \
	unity_executable="$(UNITY_EXECUTABLE)"; \
	if [[ -z "$$unity_executable" ]]; then \
		if [[ -z "$(UNITY_DATA_DIR)" ]]; then \
			echo 'Set UNITY_EXECUTABLE or UNITY_DATA_DIR before running tests.' >&2; \
			exit 2; \
		fi; \
		unity_executable="$$(cd "$(UNITY_DATA_DIR)/.." && pwd)/Unity"; \
	fi; \
	if [[ ! -x "$$unity_executable" ]]; then \
		echo "Unity executable was not found or is not executable: $$unity_executable" >&2; \
		exit 2; \
	fi; \
	"$$unity_executable" -batchmode -nographics -projectPath "$(TEST_PROJECT)" -runTests -testPlatform EditMode -testResults "$(TEST_RESULTS)" -logFile "$(TEST_LOG)"

verify: test ## Build all DLLs and run the complete Edit Mode validation suite.
	@printf 'Verification complete. Results: %s\n' "$(TEST_RESULTS)"

list-dlls: ## List distributable DLL paths and file sizes.
	@ls -lh Editor/Nvh.UvTileScanner.Core.Editor.dll Editor/Nvh.UvTileScanner.Poiyomi.Editor.dll Editor/Nvh.UvTileScanner.Editor.dll

require-unity-data:
	@if [[ -z "$(UNITY_DATA_DIR)" || ! -f "$(UNITY_DATA_DIR)/Managed/UnityEditor.dll" ]]; then \
		echo 'Set UNITY_DATA_DIR to a Unity Editor/Data directory containing Managed/UnityEditor.dll.' >&2; \
		exit 2; \
	fi

require-test-project:
	@if [[ ! -f "$(TEST_PROJECT)/ProjectSettings/ProjectVersion.txt" ]]; then \
		echo "Unity validation project not found: $(TEST_PROJECT)" >&2; \
		echo 'Set TEST_PROJECT to a Unity project with the Unity Test Framework installed.' >&2; \
		exit 2; \
	fi
