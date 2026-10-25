// Builds "share to..." links. Every value goes through encodeURIComponent, so a recipe title
// can't break out of the URL or inject extra parameters.
export interface ShareTarget {
  name: string;
  url: string;
}

export function shareTargets(link: string, title: string): ShareTarget[] {
  const u = encodeURIComponent(link);
  const t = encodeURIComponent(title);
  const text = encodeURIComponent(`${title}: a recipe I want to share`);
  return [
    { name: 'Facebook', url: `https://www.facebook.com/sharer/sharer.php?u=${u}` },
    { name: 'X', url: `https://x.com/intent/post?url=${u}&text=${t}` },
    { name: 'Reddit', url: `https://www.reddit.com/submit?url=${u}&title=${t}` },
    { name: 'Pinterest', url: `https://www.pinterest.com/pin/create/button/?url=${u}&description=${t}` },
    { name: 'Email', url: `mailto:?subject=${t}&body=${text}%0A%0A${u}` }
  ];
}
