@api @L2
Feature: Sending the group pre-order to the restaurant

  When the guests are done, the owner sends everything to the restaurant as one
  pre-order. Only the carts the guests have submitted go in, and a dish ordered
  by several people becomes one line with the quantities added up. The booking's
  pre-order is then submitted by the customer, and the group is closed for
  changes.

  Background:
    Given the restaurant's menu has the dishes:
      | dish       | price |
      | Borscht    | 12.50 |
      | Khachapuri | 9.00  |
    And a customer has booked a table for 4 guests

  Rule: The owner sends the submitted carts as one pre-order

    @smoke
    Scenario: The same dishes are added up and carts still open are left out
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
      Then the pre-order is sent to the restaurant
      And the restaurant gets the pre-order:
        | dish       | quantity |
        | Borscht    | 3        |
        | Khachapuri | 2        |

  Rule: Nothing is sent while there is nothing to send

    @negative
    Scenario: No guest has submitted a cart
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      When the owner submits the group pre-order
      Then the owner is told the pre-order cannot be sent
      And the group pre-order is still open

    @negative
    Scenario: The owner has no pre-order of their own
      Given "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      When the owner submits the group pre-order
      Then the owner is told the pre-order cannot be sent
      And the group pre-order is still open

    @negative
    Scenario: The group does not exist
      Given the owner has pre-ordered 1 "Khachapuri"
      When the owner submits the pre-order of a group that does not exist
      Then the owner is told the group could not be found

    @negative
    Scenario: The booking does not exist
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      When the owner submits the group pre-order for a booking that does not exist
      Then the owner is told the pre-order cannot be sent
      And the group pre-order is still open

  # "Submitted carts without dishes" cannot happen through the product: an empty cart cannot be
  # submitted, and a submitted cart cannot be changed. The service still refuses such a group (see
  # README), but these two scenarios show the state is never reached.
  Rule: A submitted cart always has dishes in it

    @negative
    Scenario: A guest cannot submit an empty cart
      Given "Anna" has joined the group
      When "Anna" tries to submit the cart
      Then the guest is told the cart is empty

    @negative
    Scenario: A guest cannot empty a cart that is already submitted
      Given "Anna" has joined the group
      And "Anna" has added 1 "Borscht" to the cart
      And "Anna" has submitted the cart
      When "Anna" tries to take 1 "Borscht" out of the cart
      Then the guest is told the submitted cart cannot be changed

  Rule: A group pre-order is sent only once

    @negative
    Scenario: The owner sends the pre-order a second time
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      And the owner has submitted the group pre-order
      When the owner submits the group pre-order
      Then the owner is told the pre-order has already been sent
      And the restaurant gets the pre-order:
        | dish       | quantity |
        | Borscht    | 2        |
        | Khachapuri | 1        |

  Rule: Only the owner can send the pre-order

    @negative
    Scenario Outline: Someone other than the owner tries to send the pre-order
      Given the owner has pre-ordered 1 "Khachapuri"
      And "Anna" has joined the group
      And "Anna" has added 2 "Borscht" to the cart
      And "Anna" has submitted the cart
      When <someone> submits the group pre-order
      Then they are told <answer>
      And the group pre-order is still open

      Examples: People who are signed in
        | someone          | answer                          |
        | another customer | they may not send the pre-order |
        | the waiter       | they may not send the pre-order |

      Examples: People who are not signed in
        | someone                        | answer           |
        | a guest of the group           | to sign in first |
        | a visitor who is not signed in | to sign in first |
