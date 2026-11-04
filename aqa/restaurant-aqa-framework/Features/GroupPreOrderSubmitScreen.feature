@ui @L2
Feature: Sending the group pre-order from the owner's screen

  The owner sends the group pre-order from the group pre-order screen and sees
  that it went to the restaurant. The waiter serving the booking then sees the
  same dishes in the pre-order of that booking.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests

  Rule: The owner sends the submitted carts from the screen

    @smoke
    Scenario: The owner sends the pre-order and sees it went to the restaurant
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Borscht" to the cart
      And "Ben" has added 1 "Khachapuri" to the cart
      And "Ben" has submitted the cart
      And "Chris" has joined the group
      And "Chris" has added 3 "Khachapuri" to the cart
      When the owner submits the group pre-order
      Then the owner sees that the pre-order was sent to the restaurant
      And the consolidated pre-order on the screen is:
        | dish       | quantity |
        | Borscht    | 3        |
        | Khachapuri | 2        |
      And the restaurant gets the pre-order:
        | dish       | quantity |
        | Borscht    | 3        |
        | Khachapuri | 2        |

    Scenario: The owner cannot send anything before a cart is submitted
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      When the owner looks at the participants of the group
      Then the owner cannot send the pre-order yet

    @negative
    Scenario: The owner is told when the pre-order cannot be sent
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      When the owner submits the group pre-order
      Then the owner is told the pre-order cannot be sent
      And the group pre-order is still open

  Rule: The waiter gets what the owner sent

    Scenario: The waiter sees the pre-order the owner sent
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      And "Ben" has joined the group
      And "Ben" has added 1 "Khachapuri" to the cart
      And the owner has submitted the group pre-order
      When the waiter opens the pre-order of the booking
      Then the waiter sees the dishes:
        | dish       | quantity |
        | Borscht    | 2        |
        | Khachapuri | 1        |
