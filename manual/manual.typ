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

A video is a document in your archive wearing the *Video* mask. Set its *Published* field to `yes` and PeerTube clients
play it from your video host, streamed straight from storage; set it back to `no` and it is gone from the clients at
once. The folder the video is in is its channel. Whoever may edit the video may publish it.

= Setting it up

+ Under *Modules ▸ Tube*, enter the *Video host* (e.g. `video.example.com`) your proxy sends to the installation's
  `peertube` prefix, and the instance's name and description clients show.
+ File a video (an MP4 plays everywhere) in any folder, give it the *Video* mask, fill *Description* and, when you release
  it, *Published* = `yes` and *Published at* (the catalogue lists newest first). A *Thumbnail* is the id of an image
  document; *Duration seconds*, *Language* and *Tags* are shown by clients.

= Limits

The module serves tenants that do not encrypt content at rest: a video's bytes go straight from storage to the player,
and an encrypted object could only be served through the server. Such a tenant sees an empty catalogue, and the log says
why. Files play as stored; there is no transcoding.
