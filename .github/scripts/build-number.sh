#!/usr/bin/env bash
# The build number a release is made with: one rule for every workflow that needs it (issue #203,
# step 7). The desktop release (deploy-wallpaper-service.yml) passes its own ref name and run
# number; the Android package workflow passes those of the desktop release run it follows, so the
# desktop and the phone made from one commit carry the same number.
#
#   build-number.sh <ref name> <run number>
#
# A ref name that is a "vN" tag (v5, v5-hotfix) gives N; any other ref name (main, a branch) gives
# the run number. Prints the number in decimal and nothing else. Anything that cannot give a
# positive whole number fails, so a workflow stops before it publishes a release with a wrong or
# empty number.
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: build-number.sh <ref name> <run number>" >&2
  exit 2
fi

ref_name="$1"
run_number="$2"

# The short ref name (github.ref_name, workflow_run.head_branch), never the full refs/... form
if [[ "$ref_name" == refs/* ]]; then
  echo "build-number.sh: expected a short ref name such as main or v5, got '$ref_name'" >&2
  exit 1
fi

if [[ "$ref_name" =~ ^v([0-9]+) ]]; then
  number="${BASH_REMATCH[1]}"
else
  number="$run_number"
fi

if [[ ! "$number" =~ ^[0-9]+$ ]]; then
  echo "build-number.sh: '$number' (from ref '$ref_name', run '$run_number') is not a whole number" >&2
  exit 1
fi

# Decimal, without leading zeros (v007 gives 7, as the version 007.0.0 would be read). The
# number is the major part of a .NET version, so it is at most 2147483647; longer digit strings
# are refused before bash's arithmetic could overflow on them.
number="${number#"${number%%[!0]*}"}"
if [[ -z "$number" || ${#number} -gt 10 ]] || (( number < 1 || number > 2147483647 )); then
  echo "build-number.sh: the build number must be from 1 to 2147483647, got '${number:-0}' (from ref '$ref_name', run '$run_number')" >&2
  exit 1
fi

echo "$number"
