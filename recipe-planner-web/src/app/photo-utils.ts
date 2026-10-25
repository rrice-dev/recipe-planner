export class PhotoReadError extends Error {}

// Shrinks the photo in the browser before uploading: much faster on mobile data, and the
// server would shrink it anyway. Drawing to a canvas also drops the photo's EXIF data
// (including GPS location) before it ever leaves the phone. The server still re-checks everything.
export async function shrinkPhoto(file: File, maxEdge = 1024, quality = 0.85): Promise<Blob> {
  let source: ImageBitmap;
  try {
    source = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    throw new PhotoReadError();
  }

  const scale = Math.min(1, maxEdge / Math.max(source.width, source.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(source.width * scale));
  canvas.height = Math.max(1, Math.round(source.height * scale));
  canvas.getContext('2d')!.drawImage(source, 0, 0, canvas.width, canvas.height);
  source.close();

  const blob = await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/jpeg', quality));
  if (!blob) throw new PhotoReadError();
  return blob;
}
