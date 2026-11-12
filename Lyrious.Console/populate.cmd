Register rowe ASDqwe123_ "Robert Westman"
Register tojo ASDqwe123_ "Torbjörn Jonsson"
Register mabe ASDqwe123_ "Maria Berggren"
Register anlo ASDqwe123_ "Andreas Löfqvist"
Register tope ASDqwe123_ "Tobias Pettersson"
Register rojo ASDqwe123_ "Robert Jonsson"
Register hebe ASDqwe123_ "Helena Bertholdsson"
Register mapo ASDqwe123_ "Markus Porsklev"
Register magy ASDqwe123_ "Mattias Gyllengahm"
Register chri ASDqwe123_ "Chrille"
Register frlu ASDqwe123_ "Fredrik Lundqvist"

Login rowe ASDqwe123_
	CreateGroup Jonzons

	InviteMember mabe Conductor
	InviteMember tojo
	InviteMember anlo
	InviteMember tope

	CreateSong "The Best" F
	CreateSong "Burning Love" G
	CreateSong "Bye Bye Jonny" G#
	CreateSong "Back in black" E
	CreateSong "Blackbird" Em

	CreateSetlist "Replista -25"
	AddToSetlist "Replista -25" "The Best" "Burning Love" "Bye Bye Jonny" "Back in black" "Blackbird"

	CreateGroup "Norra Glädjen"

	InviteMember rojo Conductor
	InviteMember hebe
	InviteMember mapo
Logout

Login tojo ASDqwe123_
	ApproveMembership Jonzons
	JoinGroup Jonzons
Logout

Login anlo ASDqwe123_
	ApproveMembership Jonzons
	JoinGroup Jonzons
Logout

Login mabe ASDqwe123_
	ApproveMembership Jonzons
	JoinGroup Jonzons
	# Positions are 0-based: 2 is the third song
	PlayFromSetlist "Replista -25" 2
Logout

Login rojo ASDqwe123_
	ApproveMembership "Norra Glädjen"
Logout

Login hebe ASDqwe123_
	ApproveMembership "Norra Glädjen"
Logout

Login mapo ASDqwe123_
	ApproveMembership "Norra Glädjen"
Logout

Login magy ASDqwe123_
	CreateGroup Utmarken
	InviteMember rojo Conductor
	InviteMember chri

	CreateGroup "Captain Crew"
	InviteMember frlu Admin
	InviteMember tope

	CreateGroup "Native Sons"
Logout
