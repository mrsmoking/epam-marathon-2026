@ui @L3
Feature: Removing a participant on the owner's screen

  The owner removes a guest from the group pre-order screen: the guest's card
  disappears and the totals are recalculated. A guest removed before the
  pre-order is sent leaves nothing behind in what the restaurant and the waiter
  get.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests

  Rule: The owner removes a guest on the screen

    Scenario: The removed guest disappears and the totals are recalculated
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Khachapuri" to the cart
      And "Ben" has submitted the cart
      When the owner looks at the participants of the group
      And the owner removes "Ben" from the group
      Then the owner sees the participants:
        | participant | dishes      | total | cart          |
        | the owner   | nothing     | 0     | not submitted |
        | Anna        | 2 x Borscht | 25.00 | submitted     |
      And the grand total of the submitted carts is 25.00

  Rule: What the restaurant gets leaves the removed guest out

    @e2e @smoke
    Scenario: From the guests' carts to the waiter, without the removed guest
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Borscht" to the cart
      And "Ben" has added 1 "Khachapuri" to the cart
      And "Ben" has submitted the cart
      And "Chris" has joined the group
      And "Chris" has added 1 "Borscht" to the cart
      And "Chris" has submitted the cart
      When the owner looks at the participants of the group
      And the owner removes "Ben" from the group
      And the owner submits the group pre-order
      Then the owner sees that the pre-order was sent to the restaurant
      And the restaurant gets the pre-order:
        | dish       | quantity |
        | Borscht    | 3        |
        | Khachapuri | 1        |
      When the waiter opens the pre-order of the booking
      Then the waiter sees the dishes:
        | dish       | quantity |
        | Borscht    | 3        |
        | Khachapuri | 1        |
