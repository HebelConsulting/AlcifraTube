#!/usr/bin/env bash
# The client check's verdict as an issue (core #2142): one open issue per failing client, naming the client version and
# the run; a green run closes it. Called by the workflow with GH_TOKEN set.
#
#   report.sh <client> <what was tested> <outcome>     # outcome: success | failure | skipped | cancelled
set -euo pipefail
client=$1; tested=$2; outcome=$3
title="Client check: $client no longer reaches the module"
run="$GITHUB_SERVER_URL/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
existing=$(gh issue list --state open --search "\"$title\" in:title" --json number --jq '.[0].number // empty')

case "$outcome" in
  success)
    if [ -n "$existing" ]; then
      gh issue close "$existing" --comment "Green again with $tested ($run)."
      echo "closed #$existing"
    fi
    ;;
  *)
    body="The weekly check failed with **$tested** (outcome: $outcome). The run and its log: $run

Read the failing step there: a model that reads a field this module does not answer, or a request the module refuses. Core ADR 1004 and tube ADR 0004 hold the contract; a client version that reads more is a new row there, never a guess."
    if [ -n "$existing" ]; then
      gh issue comment "$existing" --body "$body"
      echo "commented on #$existing"
    else
      gh issue create --title "$title" --body "$body"
    fi
    ;;
esac
