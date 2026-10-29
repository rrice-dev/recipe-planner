#!/usr/bin/env bash
# Deletes old images from the container registry, keeping the newest $KEEP of each app.
# Every deploy pushes two new images, and the registry's Basic plan includes 10 GB, so without this
# old images pile up and start costing extra.
#
# Run by the deploy (after the live site check passes). To see what it WOULD delete without
# deleting anything, run it yourself while signed in to Azure:
#   DRY_RUN=1 ACR=recipeplanner516 ./scripts/prune-images.sh
# (RG defaults to recipe-planner-rg; KEEP to 10)
#
# Images are built with docker buildx, which can push an index with the image and a build record
# ("attestation") inside it. Those inner manifests have no tag of their own, so a manifest is only
# deleted when it isn't one of the kept images and none of the kept images points to it.
set -euo pipefail
export AZURE_CORE_ONLY_SHOW_ERRORS=true   # hides the "acr manifest is in preview" warnings

: "${ACR:?Set ACR to the registry name}"
RG="${RG:-recipe-planner-rg}"
KEEP="${KEEP:-10}"
DRY_RUN="${DRY_RUN:-}"

# $1: what to print, $2: the image to delete
delete() {
  if [ -n "$DRY_RUN" ]; then echo "would delete $1"; else echo "deleting $1"; az acr repository delete -n "$ACR" --image "$2" --yes --output none; fi
}

for repo in recipe-api recipe-web; do
  # Tags to keep: the newest $KEEP, plus whatever each app is running right now, however old
  kept_tags=" $(az acr repository show-tags -n "$ACR" --repository "$repo" --orderby time_desc --query "[:$KEEP]" -o tsv | tr '\n' ' ')"
  # (the container apps have the same names as the image repositories)
  running=$(az containerapp show -n "$repo" -g "$RG" --query "properties.template.containers[0].image" -o tsv)
  kept_tags="$kept_tags${running##*:} "

  # Every manifest those tags point to, including an index's inner manifests
  keep_digests=" "
  for tag in $kept_tags; do
    keep_digests="$keep_digests$(az acr manifest show-metadata -r "$ACR" -n "$repo:$tag" --query digest -o tsv) "
    keep_digests="$keep_digests$(az acr manifest show -r "$ACR" -n "$repo:$tag" --query "manifests[].digest" -o tsv 2>/dev/null | tr '\n' ' ') "
  done
  echo "$repo: keeping tags$kept_tags"
  # Never go on without knowing what to keep: a failed lookup must not turn into "delete everything"
  if [ "$(echo $keep_digests | wc -w)" -lt "$(echo $kept_tags | wc -w)" ]; then
    echo "Couldn't look up every kept image, so nothing was deleted from $repo." >&2; exit 1
  fi

  # Delete whole manifests (by digest), never by tag: deleting by tag would also remove any other tag,
  # possibly a kept one, that points to the same image
  az acr manifest list-metadata -r "$ACR" -n "$repo" --query "[].[digest, join(' ', tags || \`[]\`)]" -o tsv |
  while IFS=$'\t' read -r digest tags; do
    case "$keep_digests" in *" $digest "*) continue ;; esac
    delete "$repo@$digest${tags:+ (tags: $tags)}" "$repo@$digest"
  done
done
