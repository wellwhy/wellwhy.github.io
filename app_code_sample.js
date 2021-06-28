var loadRoute = function() {
    if (location.hash==='' || location.hash==="#/home") {
        var template = Handlebars.compile(document.getElementById('home-template').innerHTML);
        document.getElementById('app').innerHTML = template({});
    } else if (location.hash==='#/about') {
        var template = Handlebars.compile(document.getElementById('about-template').innerHTML);
        document.getElementById('app').innerHTML = template({});
    } else if (location.hash==='#/help') {
        var template = Handlebars.compile(document.getElementById('help-template').innerHTML);
        document.getElementById('app').innerHTML = template({});
    } else if (location.hash.indexOf('#/search')===0) {

        //Run a search

        //To begin, store the search parameters, which have been placed in the URL in the form of the serialized search form

        var query = location.hash.substring('#/search?'.length);

        if(query != "") {
        var search_parts = query.split('&');
        console.log(query);
        var search_parameters = {};
            for (var i = 0; i < search_parts.length; i++) {
                var parameter = search_parts[i].split('=');
                var name = parameter[0];
                var value = decodeURIComponent(parameter[1]);
                if (value.trim()=='' || value.trim()=='Any') {
                    continue;
                }
                if (!search_parameters[name]) {
                    search_parameters[name] = [];
                }
                search_parameters[name].push(value);
            }
        }

        //DEBUG, printing the search parameters from the hash/URL

        console.log("SEARCH PARAMETERS FROM HASH:");
        console.log(search_parameters);

        //Now, define what search options are available, what type each field is, and which column in the CSV it represents

        //Two specific functions to be used to summarize the search results, number of unique books, and the length of all similes found summed

        var book_unique = function(result) {return result[1] + result[5];};
        var simile_length = function(result) {return result[3] - result[2]};

        //Separate search options into groups, as per client's request. Groups marked ADVANCED must be shown through a button to reveal additional options

        var search_info = {
            'groups': [
                {'name':'Work/Position Info', 'fields': [
                    {'key':'author', 'title': 'Author and Work', 'csv_posn':0, 'input_type': 'select_checkbox'},
                    {'key':'book_number', 'title':'Book Number', 'csv_posn':1, 'input_type':'number_range'},
                    {'key':'starts_at', 'title':'Starts At', 'csv_posn':2, 'input_type':'substring_number'},
                ]},
                {'name':'Simile Info', 'fields': [
                    {'key':'story_subject', 'title':'Story Subject', 'csv_posn':15, 'input_type':'select_checkbox'},
                    {'key':'simile_subject', 'title':'Simile Subject', 'csv_posn':19, 'input_type':'select_checkbox'},
                ]},
                {'name':'ADVANCED Work/Position Info', 'fields': [
                    {'key':'ends_at', 'title':'Ends At', 'csv_posn':3, 'input_type':'substring_number'},
                    {'key':'starting_metrical_foot', 'title': 'Starting Metrical Foot', 'csv_posn':4, 'input_type': 'select_checkbox'},
                    {'key':'ending_metrical_foot', 'title': 'Ending Metrical Foot', 'csv_posn':5, 'input_type': 'select_checkbox'},
                    {'key':'simile_intro', 'title':'Simile Intro', 'csv_posn':6, 'input_type':'substring'},
                    {'key':'simile_exit', 'title':'Simile Exit', 'csv_posn':7, 'input_type':'substring'},
                    {'key':'simile_length', 'title':'Simile Length (# of Verses)', 'csv_posn':8, 'input_type':'number_range'},
                ]},
                {'name':'ADVANCED Specialized Information', 'fields': [
                    {'key':'character_narrator', 'title':'Character Narrator', 'csv_posn':10, 'input_type':'select_checkbox'},
                    {'key':'human_actors', 'title': 'Human Actors', 'csv_posn':11, 'input_type': 'select_checkbox'},
                    {'key':'simile_cluster', 'title': 'Simile Cluster', 'csv_posn':12, 'input_type': 'select_checkbox'},
                    {'key':'option_simile', 'title':'Option Simile', 'csv_posn':13, 'input_type':'select_checkbox'},
                    {'key':'proper_names', 'title':'Proper Names', 'csv_posn':14, 'input_type':'select_checkbox'},
                ]},
                {'name':'ADVANCED Simile Info', 'fields': [
                    {'key':'story_details', 'title':'Story Details', 'csv_posn':16, 'input_type':'substring'},
                    {'key':'simile_details', 'title': 'Simile Details', 'csv_posn':20, 'input_type': 'substring'},
                ]},
            ]
        };

        //For "select_checkbox" fields, store every unique value for each field (CSV Column) to have their unique checkbox created.

        for (var i=0;i<search_info.groups.length;i++) {
            for (var j=0;j<search_info.groups[i].fields.length;j++) {
                var field = search_info.groups[i].fields[j];
                if (field.input_type === 'select_checkbox') {
                    field.choices = window.simile_db
                        .map(function(entry) {return entry[field.csv_posn]}) // pull the value in the column
                        .map(function(entry) {return entry.split('\n')}) // separate multi-valued fields
                        .reduce(function(acc, curr) {
                            if (Array.isArray(curr)) {
                                curr.forEach(function(c) {acc.push(c);})
                            } else {
                                acc.push(curr);
                            }
                            return acc;
                        }, []) // fold sub-arrays into elements of main array
                        .filter(filter_unique)
                        .sort();
                }
            }
        }

        // convenience function to get the search_info entry for a specific key

        var get_search_info_with_key = function(a_key) {
            for (var i=0;i<search_info.groups.length;i++) {
                for (var j=0;j<search_info.groups[i].fields.length;j++) {
                    if (search_info.groups[i].fields[j].key == a_key) {
                        return search_info.groups[i].fields[j];
                    }
                }
            }
            return null;
        };

        //Now, begin getting search results based on the hash. First, filter out empty rows of the CSV.

        var search_results = window.simile_db.filter(function(result) {
            return result.length>1;
        });

        //DEBUG, print the entire CSV for viewing in console.

        console.log("ENTIRE CSV:\n");
        console.log(search_results);

        //For each parameter the user wished to search by, filter the search results set based on those parameters.
        //Each different type of input has a different way of searching for it in the CSV.

        for (var search_name in search_parameters) {
            search_values = search_parameters[search_name];

            var search_info_entry = get_search_info_with_key(search_name);
            if (search_info_entry == null) {
                continue;
            }

            if (search_info_entry.input_type=='select_checkbox') {
                search_results = search_results.filter(function(result) {
                    var text_to_search = result[search_info_entry.csv_posn];
                    for (var i=0;i<search_values.length;i++) {
                        if (text_to_search.split('\n').indexOf(search_values[i]) != -1) {
                            return true;
                        }
                    }
                    return false;
                });
            } 
            
            else if (search_info_entry.input_type=='substring') {
               search_results = search_results.filter(function(result) {
                    var text_to_search = result[search_info_entry.csv_posn];
                    return text_to_search.toLowerCase().indexOf(search_values[0].toLowerCase()) != -1;
               });
            } 
            
            else if (search_info_entry.input_type=='substring_number') {
                search_results = search_results.filter(function(result) {
                     var text_to_search = result[search_info_entry.csv_posn];
                     return text_to_search.toLowerCase() == search_values[0].toLowerCase();
                });
            } 
            
            else if (search_info_entry.input_type=='number_range') {
               search_results = search_results.filter(function(result) {
                    var text_to_search = result[search_info_entry.csv_posn];
                    var num_to_search = parseInt(text_to_search);
                    if (search_values[0].indexOf("-")==-1) {
                        var num1 = parseInt(search_values[0]);
                        return num1==num_to_search;
                    } else {
                        var text_parts = search_values[0].split('-');
                        var num1 = parseInt(text_parts[0]);
                        var num2 = parseInt(text_parts[1]);
                        return (num1 <= num_to_search) && (num_to_search <= num2);
                    }
               });
            }
        }

        //Search result metadata, used for summarizing the search results to the user. 

        var search_result_metadata = {
            num_results: search_results.length,
            num_books: search_results.map(book_unique).filter(filter_unique).length,
            num_lines: search_results.map(simile_length).reduce(function(a,b) {return parseInt(a)+parseInt(b);}, 0)
        };

        //DEBUG the CSV has been pruned, and all valid search results are stored.

        console.log("PRUNED CSV:\n");
        console.log(search_results);

        //Now, we must find the associated checkboxes to each unique search result value.
        //All fields which are selected by checkboxes are stored, alongside their index in the CSV row.
        
        var all_checkboxes_found = [];
        var attributes_that_are_checkboxes_names = [
            "author",
            "story_subject",
            "simile_subject",
            "starting_metrical_foot",
            "ending_metrical_foot",
            "character_narrator",
            "human_actors",
            "simile_cluster",
            "option_simile",
            "proper_names"]
        var attributes_that_are_checkboxes = [0,15,19,4,5,10,11,12,13,14];

        //Then, for each search result...

        for (var i = 0; i < search_results.length; i++) {
            var single_result = search_results[i];
            var name_i = 0;

            //...go to each column which is selected by checkbox

            attributes_that_are_checkboxes.forEach(function(j) {

                //(Split CSV columns which have multiple words as their value, because they have individual checkboxes)
                
                single_result_single_attributes_array = single_result[j].split('\n')
                single_result_single_attributes_array.forEach(function(single_result_single_attribute) {

                    //...find the checkbox element...
                    
                    var name_and_value = [attributes_that_are_checkboxes_names[name_i],single_result_single_attribute];
                    if(!all_checkboxes_found.includes(name_and_value)){

                        //...and store it.

                        all_checkboxes_found.push(name_and_value);
                    }
                })

                name_i++;
            })
        }

        //DEBUG all valid checkboxes are now in all_checkboxes_found, and are printed to console.

        console.log("CHECKBOXES THAT ARE VALID:");
        console.log(all_checkboxes_found);

        //Setup the handlebars template to display the search results, grey out invalid checkboxes, and display a summarization of the results

        var template = Handlebars.compile(document.getElementById('search-template').innerHTML);
        document.getElementById('app').innerHTML = template({
            'all_checkboxes_found': all_checkboxes_found,
            'search_info': search_info,
            'search_results': search_results,
            'search_result_metadata': search_result_metadata
        });

        
        //THIS IS WHERE DYNAMIC SEARCH GETS IMPLEMENTED.
        //IN THE HTML, ALL CHECKBOXES FOUND IN RESULTS ARE KEPT CLICKABLE. (ALL_CHECKBOXES_FOUND are kept white, others are greyed)
        //RIGHT BELOW HERE, ALL CHECKBOXES THAT WERE IN THE URL (SO THEY WERE ALREADY CHECKED) ARE RECHECKED AFTER THE TEMPLATE RESET EVERYTHING

        //The handlebars template will grey out checkboxes which will return no results, but we still need to re-check checkboxes the user already selected
        //(HTML resets these on page refresh)

        //Get all HTML checkbox elements

        var checkboxes = document.querySelectorAll("input[type=checkbox]");

        //For each search parameter...

        for (var parameter_name in search_parameters) {

            //...There is an associated amount of values, each which has a checkbox.

            var parameter_values = search_parameters[parameter_name]
            for(var parameter_value in parameter_values){

                //DEBUG, state which checkbox is being checked

                console.log("Checking " + parameter_values[parameter_value] + "'s box");

                //If the checkbox was found (because some parameters don't have checkboxes, and are text entry etc.), check it!

                var found_checkbox = find_checkbox_with_value(checkboxes, parameter_name, parameter_values[parameter_value]);
                if(found_checkbox)
                    found_checkbox.checked = true;
            }
        }
        
        //Lastly, for each text entry parameter...

        var textboxes = document.querySelectorAll("input[type=text]");
        for (var textbox in textboxes) {

            //Load the previously typed text, which was wiped on page refresh.
            //(The handlebars template gives each text entry onkeyup="init_dynamic_onkeyup_substring", which saves the text typed)

            textboxes[textbox].value = get_saved_textbox_string(textboxes[textbox].id);

            //DEBUG, show what checkbox is getting what text.

            console.log("Loading " + get_saved_textbox_string(textboxes[textbox].id) + " at " + textboxes[textbox].id);
        }

    } else {
        var template = Handlebars.compile(document.getElementById('notfound-template').innerHTML);
        document.getElementById('app').innerHTML = template({});
    }
};

function default_sort(rows) {
    // default sort order: author (col 0), work (col 1), book_number (col 5), start_verse (col 6)
    var sort_cols = [0,1,5,6];
    var sort_cols_number = [5,6];
    return rows.sort(function(row1, row2) {
        for (var sort_col_key in sort_cols) {
            var col_posn = sort_cols[sort_col_key];
            if (col_posn===5 || col_posn===6) {
                if (row1[col_posn]!==row2[col_posn]) {
                    return parseInt(row1[col_posn]) - parseInt(row2[col_posn]);
                }
            } else {
                if (row1[col_posn]!==row2[col_posn]) {
                    return row1[col_posn].localeCompare(row2[col_posn]);
                }
            }
        }
        return 0;
    });
}
Papa.parse("SimilesJun7.csv", {
    download:true,
    skipEmptyLines:true,
    complete: function(results) {
        for (var i=0;i<results.data.length;i++) {
            for (var j=0;j<results.data[i].length;j++) {
                results.data[i][j] = results.data[i][j].trim();
            }
        }
        window.simile_db_headers = results.data[0];
        window.simile_db = default_sort(results.data.slice(1));
        loadRoute();
    }
});
window.onhashchange = loadRoute;


function init_dynamic_onclick_checkbox(){
        location.hash = '#/search?' + $('#search_form').serialize();
}

function init_dynamic_onkeyup_substring(textbox){
    console.log("Saving " + textbox.value + " at " + textbox.id);
    var id = textbox.id;  // get the sender's id to save it . 
    var val = textbox.value; // get the value. 
    localStorage.setItem(id, val);// Every time user writing something, the localStorage's value will override.
    location.hash = '#/search?' + $('#search_form').serialize();
}

function get_saved_textbox_string(v){
    if (!localStorage.getItem(v)) {
        return "";
    }
    return localStorage.getItem(v);
}

function find_checkbox_with_value(checkboxes, parameter, value){
    for(i = 0; i < checkboxes.length; i++){
        // console.log(checkboxes[i]);
        if(checkboxes[i].value == value && checkboxes[i].name == parameter)
            return checkboxes[i];
    }
    return null;
}