device-pda-slot-component-slot-name-cartridge = Cartridge

default-program-name = Program
notekeeper-program-name = Notekeeper
nano-task-program-name = NanoTask
messenger-program-name = Messenger
# Radiant Sector
messenger-profile-photo = Profile photo
messenger-profile-photo-tooltip = Choose a profile photo from this PDA
messenger-profile-photo-entry = Saved photo { $number }
messenger-profile-photo-capture = Take profile photo now
# Radiant Sector
messenger-profile-photo-remove = Remove profile photo
# Radiant Sector
pda-camera-program-name = PDA camera
pda-camera-capture = Take photo
pda-camera-capture-tooltip = Save a photo to this PDA
pda-camera-mode-normal = Rear camera
pda-camera-mode-selfie = Selfie camera
pda-camera-mode-tooltip = Switch between rear and selfie cameras
pda-camera-photo-count = Saved photos: { $count }
messenger-contacts = Contacts
messenger-requests = Contact requests
messenger-discover = People in the network
messenger-add-contact = Add friend
messenger-find-players = Find players
messenger-refresh-players = Refresh
messenger-notifications-on = Alerts: on
messenger-notifications-off = Alerts: off
messenger-notifications-tooltip = Turn message alerts on or off for this ID card
messenger-notification-title = New message
messenger-notification-message = New message
messenger-select-chat = Select a chat to start messaging
messenger-no-messages = No messages yet
messenger-friends = Friends
messenger-dialogs = Dialogs
messenger-empty-friends = No friends yet.
messenger-open-chat = Open chat
messenger-remove-friend = Remove
messenger-create-group = Create chat
messenger-group-title = New group chat
messenger-group-name = Chat name
messenger-contact-request-title = Friend request
messenger-contact-request-message = { $name } wants to add you as a friend.
# Radiant Sector
messenger-contact-accepted-title = Friend request accepted
messenger-contact-accepted-message = { $name } accepted your friend request.
messenger-contact-removed-title = Friend removed
messenger-contact-removed-message = { $name } removed you from friends.
messenger-accept = Accept
messenger-decline = Decline
messenger-back = Back
messenger-send = Send
messenger-placeholder = Write a message…
messenger-chats = Chats
messenger-contact = Contact
messenger-group = Group chat
messenger-empty-chats = No dialogs yet.
messenger-no-users = No users online.
news-read-program-name = Station news

crew-manifest-program-name = Crew manifest
crew-manifest-cartridge-loading = Loading ...

net-probe-program-name = NetProbe
net-probe-scan = Scanned {$device}!
net-probe-label-name = Name
net-probe-label-address = Address
net-probe-label-frequency = Frequency
net-probe-label-network = Network

log-probe-program-name = LogProbe
log-probe-scan = Downloaded logs from {$device}!
log-probe-label-time = Time
log-probe-label-accessor = Accessed by
log-probe-label-number = #
log-probe-print-button = Print Logs
log-probe-printout-device = Scanned Device: {$name}
log-probe-printout-header = Latest logs:
log-probe-printout-entry = #{$number} / {$time} / {$accessor}

astro-nav-program-name = AstroNav

med-tek-program-name = MedTek

# NanoTask cartridge

nano-task-ui-heading-high-priority-tasks =
    { $amount ->
        [zero] No High Priority Tasks
        [one] 1 High Priority Task
       *[other] {$amount} High Priority Tasks
    }
nano-task-ui-heading-medium-priority-tasks =
    { $amount ->
        [zero] No Medium Priority Tasks
        [one] 1 Medium Priority Task
       *[other] {$amount} Medium Priority Tasks
    }
nano-task-ui-heading-low-priority-tasks =
    { $amount ->
        [zero] No Low Priority Tasks
        [one] 1 Low Priority Task
       *[other] {$amount} Low Priority Tasks
    }
nano-task-ui-done = Done
nano-task-ui-revert-done = Undo
nano-task-ui-priority-low = Low
nano-task-ui-priority-medium = Medium
nano-task-ui-priority-high = High
nano-task-ui-cancel = Cancel
nano-task-ui-print = Print
nano-task-ui-delete = Delete
nano-task-ui-save = Save
nano-task-ui-new-task = New Task
nano-task-ui-description-label = Description:
nano-task-ui-description-placeholder = Get something important
nano-task-ui-requester-label = Requester:
nano-task-ui-requester-placeholder = John Nanotrasen
nano-task-ui-item-title = Edit Task
nano-task-printed-description = [bold]Description[/bold]: {$description}
nano-task-printed-requester = [bold]Requester[/bold]: {$requester}
nano-task-printed-high-priority = [bold]Priority[/bold]: [color=red]High[/color]
nano-task-printed-medium-priority = [bold]Priority[/bold]: Medium
nano-task-printed-low-priority = [bold]Priority[/bold]: Low

# Wanted list cartridge
wanted-list-program-name = Wanted list
wanted-list-label-no-records = It's all right, cowboy
wanted-list-search-placeholder = Search by name and status

wanted-list-age-label = [color=darkgray]Age:[/color] [color=white]{$age}[/color]
wanted-list-job-label = [color=darkgray]Job:[/color] [color=white]{$job}[/color]
wanted-list-species-label = [color=darkgray]Species:[/color] [color=white]{$species}[/color]
wanted-list-gender-label = [color=darkgray]Gender:[/color] [color=white]{$gender}[/color]

wanted-list-reason-label = [color=darkgray]Reason:[/color] [color=white]{$reason}[/color]
# Radiant sector start
wanted-list-shuttle-label = [color=darkgray]Shuttle:[/color] [color=white]{ $shuttle }[/color]
wanted-list-no-shuttle-label = [color=darkgray]Shuttle:[/color] [color=white]none[/color]
# Radiant sector end
wanted-list-unknown-reason-label = unknown reason

wanted-list-initiator-label = [color=darkgray]Initiator:[/color] [color=white]{$initiator}[/color]
wanted-list-unknown-initiator-label = unknown initiator

wanted-list-status-label = [color=darkgray]status:[/color] {$status ->
        [suspected] [color=yellow]suspected[/color]
        [wanted] [color=red]wanted[/color]
        [detained] [color=#b18644]detained[/color]
        [paroled] [color=green]paroled[/color]
        [discharged] [color=green]discharged[/color]
        *[other] none
    }

wanted-list-history-table-time-col = Time
wanted-list-history-table-reason-col = Crime
wanted-list-history-table-initiator-col = Initiator
# Radiant Sector
messenger-send-photo = Send photo
messenger-send-photo-capture = Take and send photo
# Radiant Sector
pda-camera-gallery = Gallery
pda-camera-camera = Camera
pda-camera-gallery-tooltip = View photos stored on this PDA
# Radiant Sector
pda-camera-pan-left = Move frame left
pda-camera-pan-up = Move frame up
pda-camera-pan-down = Move frame down
pda-camera-pan-right = Move frame right
pda-camera-zoom-out = Zoom out
pda-camera-zoom-in = Zoom in
# Radiant Sector
pda-camera-gallery-grid = Grid
pda-camera-gallery-open = Open photo
pda-camera-gallery-previous = Previous photo
pda-camera-gallery-next = Next photo
pda-camera-gallery-delete = Delete photo
