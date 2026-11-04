#import "template.typ": conf, accent

#show: doc => conf(doc)

#page(numbering: none, footer: none)[
  #set align(center + horizon)
  #block[
    #text(size: 34pt, weight: "bold", fill: accent)[Alcifra]
    #v(0.2em)
    #text(size: 18pt)[Tube — Module Manual]
    #v(1.2em)
    #text(size: 11pt, fill: gray)[Your own videos on your own installation, played by PeerTube clients]
  ]
]

= What the module does

A *Tube Channel* is a folder that clients show as one channel, and a repository can hold several. A video is a document
wearing the *Video* mask. Prepare it anywhere; it is offered only through a channel, by sitting in one or by a reference
placed in one, so one video can be in several channels. With its *Published* field at `yes`, PeerTube clients play it from
your video host, streamed straight from storage; set it back to `no`, or remove it from the channel, and it is gone from
the clients at once. Outside every channel a video is shown nowhere, whatever *Published* says.

Publishing takes two rights: editing the video, and creating in the channel. In the clients' overview a video appears once
per channel it is in; opening one names the channel it sits in, else the one that referenced it first.

= Setting it up

+ Under *Modules ▸ Tube*, enter the *Video host* (e.g. `video.example.com`) your proxy sends to the installation's
  `peertube` prefix, and the instance's name and description clients show.
+ Create a folder with the *Tube Channel* mask. It holds videos and their thumbnails, and references to them. Its
  *Description* is shown by clients.
+ File a video in the channel or anywhere else, with the *Video* mask: `.mp4`, `.m4v`, `.webm`, `.mov`, `.mkv` or `.ogv`.
  An MP4 plays everywhere; other types depend on the client, because files play as stored. Fill *Description* and, when you
  release it, *Published* = `yes` and *Published at* (the catalogue lists newest first). *Duration seconds*, *Language* and
  *Tags* are shown by clients.
+ A *Thumbnail* points at a document with the *Video Thumbnail* mask (`.jpg`, `.jpeg`, `.png`, `.webp`), wherever it lies.
+ If the video was prepared outside a channel, place a reference to it in the channel. The module may then read that video
  and its thumbnail, and nothing else in that place.
+ The module also needs read access to the channel itself: grant *Module: Tube* on the channel or on a folder above it.

= The pages in a browser

The video host's front page shows the latest videos, the newest in the upper left corner and the oldest in the lower right,
24 to a page, each with its thumbnail and description. Each video links to its channel's page, which shows that channel's
latest videos the same way. A video in two channels appears once for each. The front page links to a short FAQ on watching
in a mobile app.

The proxy in front of the video host has to pass `/`, `/c/` and `/faq` to the module as well, and each thumbnail counts
against the limit on anonymous requests per address.

= The Tube tab

Where the module is active, the clients gain a *Tube* tab or tile. It lists the channels you can see, each with a checkbox
and a lock if you can't add to it, and a filter over the videos. Your own rights decide what shows: published videos, and
any video you may edit, marked as a draft.

Where your rights allow, you can publish or unpublish a video, edit its details, place it in another channel, and remove it
from a channel that only references it, which never deletes the video. Channels are created, renamed and deleted in the
Repositories tree. The web and the phone play in the tab; the desktop opens the watch page in the browser, so a draft has
nothing to open there. A tenant that encrypts at rest sees the tab empty, with a line saying why.

= Limits

The module serves tenants that do not encrypt content at rest: a video's bytes go straight from storage to the player,
and an encrypted object could only be served through the server. Such a tenant sees an empty catalogue, and the log says
why. Files play as stored; there is no transcoding.
